namespace OpenPolytopia.Common.Network;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Packets;

/// <summary>
/// Client-side connection to the game server
/// </summary>
/// <remarks>
/// Received packets are queued in <see cref="IncomingPackets"/> to let the consumer
/// process them on its own thread; <see cref="KeepAlivePacket"/> gets answered automatically
/// and the connection gets closed if the server doesn't send anything for longer than <see cref="TIMEOUT"/>.
/// Every connection that leaves the machine goes through TLS, validated against the certificate store
/// of the system; there is no plaintext fallback, a server with a bad certificate is a server we don't talk to
/// </remarks>
/// <param name="address">the host name or ip address of the server</param>
/// <param name="port">the port of the server</param>
/// <param name="useTls">
/// true to wrap the connection in TLS, false for plaintext;
/// null to decide from <paramref name="address"/>, meaning TLS for everything but loopback
/// </param>
public class ClientConnection(string address, int port, bool? useTls = null) : IDisposable {
  private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(30);
  private static readonly TimeSpan TIMEOUT_CHECK_INTERVAL = TimeSpan.FromSeconds(5);

  private readonly TcpClient _client = new();
  private readonly CancellationTokenSource _cts = new();
  private NetworkConnection? _connection;

  /// <summary>
  /// true when the connection to the server is wrapped in TLS
  /// </summary>
  public bool UsesTls { get; } = useTls ?? !IsLoopback(address);

  /// <summary>
  /// Packets received from the server, waiting to be processed
  /// </summary>
  public ConcurrentQueue<IPacket> IncomingPackets { get; } = new();

  /// <summary>
  /// Fired when the connection to the server gets closed
  /// </summary>
  public event Action? OnDisconnected;

  /// <summary>
  /// true while connected to the server
  /// </summary>
  public bool Connected => _connection?.Connected ?? false;

  /// <summary>
  /// Connects to the server and starts reading packets in background
  /// </summary>
  /// <exception cref="AuthenticationException">
  /// if the server doesn't present a certificate this machine trusts for <paramref name="address"/>
  /// </exception>
  public async Task ConnectAsync() {
    PacketRegistrar.RegisterAllPackets();
    await _client.ConnectAsync(address, port, _cts.Token);

    var stream = UsesTls ? await AuthenticateAsync() : null;

    _connection = new NetworkConnection(0, _client, stream);
    _connection.OnPacketReceived += PacketReceivedAsync;
    _connection.OnDisconnected += _ => OnDisconnected?.Invoke();

    // read packets and watch for a dead server in background
    _ = _connection.RunAsync(_cts.Token);
    _ = TimeoutLoopAsync(_connection, _cts.Token);
  }

  /// <summary>
  /// Sends a packet to the server
  /// </summary>
  /// <param name="packet">the packet to send</param>
  public async Task SendPacketAsync(IPacket packet) {
    if (_connection == null) {
      return;
    }

    await _connection.SendPacketAsync(packet, _cts.Token);
  }

  /// <summary>
  /// Closes the connection
  /// </summary>
  public void Disconnect() {
    _cts.Cancel();
    _connection?.Close();
  }

  /// <summary>
  /// Checks if an address points to this same machine
  /// </summary>
  /// <remarks>
  /// Loopback traffic never leaves the machine, so it's the only case where plaintext is acceptable;
  /// anything that can't be recognized as loopback gets treated as remote and encrypted
  /// </remarks>
  /// <param name="address">the host name or ip address to check</param>
  private static bool IsLoopback(string address) =>
    string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase) ||
    (IPAddress.TryParse(address, out var ip) && IPAddress.IsLoopback(ip));

  /// <summary>
  /// Wraps the connection in TLS
  /// </summary>
  /// <remarks>
  /// The certificate gets validated the standard way, against the trust store of the operating system;
  /// a failure throws and leaves the connection unusable, it never falls back to plaintext
  /// </remarks>
  /// <returns>the authenticated stream</returns>
  private async Task<Stream> AuthenticateAsync() {
    var ssl = new SslStream(_client.GetStream(), false);

    try {
      await ssl.AuthenticateAsClientAsync(
        new SslClientAuthenticationOptions {
          TargetHost = address, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, _cts.Token);
    }
    catch {
      await ssl.DisposeAsync();
      throw;
    }

    return ssl;
  }

  private static async Task TimeoutLoopAsync(NetworkConnection connection, CancellationToken ct) {
    using var timer = new PeriodicTimer(TIMEOUT_CHECK_INTERVAL);

    try {
      while (await timer.WaitForNextTickAsync(ct)) {
        // the server pings every 10 seconds, a long silence means it's gone
        if (DateTime.UtcNow - connection.LastReceived > TIMEOUT) {
          connection.Close();
          return;
        }
      }
    }
    catch (OperationCanceledException) {
      // client disconnecting
    }
  }

  private async Task PacketReceivedAsync(NetworkConnection connection, IPacket packet) {
    // echo keep alive packets back, everything else goes to the queue
    if (packet is KeepAlivePacket keepAlive) {
      await connection.SendPacketAsync(keepAlive, _cts.Token);
      return;
    }

    IncomingPackets.Enqueue(packet);
  }

  public void Dispose() {
    Disconnect();
    _cts.Dispose();
    _connection?.Dispose();
    _client.Dispose();
    GC.SuppressFinalize(this);
  }
}
