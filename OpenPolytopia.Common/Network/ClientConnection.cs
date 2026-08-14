namespace OpenPolytopia.Common.Network;

using System.Collections.Concurrent;
using System.Net.Sockets;
using Packets;

/// <summary>
/// Client-side connection to the game server
/// </summary>
/// <remarks>
/// Received packets are queued in <see cref="IncomingPackets"/> to let the consumer
/// process them on its own thread; <see cref="KeepAlivePacket"/> gets answered automatically
/// and the connection gets closed if the server doesn't send anything for longer than <see cref="TIMEOUT"/>
/// </remarks>
public class ClientConnection(string address, int port) : IDisposable {
  private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(30);
  private static readonly TimeSpan TIMEOUT_CHECK_INTERVAL = TimeSpan.FromSeconds(5);

  private readonly TcpClient _client = new();
  private readonly CancellationTokenSource _cts = new();
  private NetworkConnection? _connection;

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
  public async Task ConnectAsync() {
    PacketRegistrar.RegisterAllPackets();
    await _client.ConnectAsync(address, port, _cts.Token);

    _connection = new NetworkConnection(0, _client);
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
