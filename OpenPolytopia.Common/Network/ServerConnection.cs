namespace OpenPolytopia.Common.Network;

using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using Packets;

/// <summary>
/// Accepts TCP connections and manages one <see cref="NetworkConnection"/> per client
/// </summary>
/// <remarks>
/// Every <see cref="KEEP_ALIVE_INTERVAL"/> it sends a <see cref="KeepAlivePacket"/> to every client
/// and disconnects the ones that didn't send anything back for longer than <see cref="TIMEOUT"/>
/// </remarks>
/// <param name="port">the port to listen on</param>
/// <param name="bindAddress">the ip address to bind to; null to listen on every interface</param>
public class ServerConnection(int port, string? bindAddress = null) : IDisposable {
  private static readonly TimeSpan KEEP_ALIVE_INTERVAL = TimeSpan.FromSeconds(10);
  private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(30);

  private readonly TcpListener _listener = bindAddress == null
    ? TcpListener.Create(port)
    : new TcpListener(System.Net.IPAddress.Parse(bindAddress), port);
  private readonly ConcurrentDictionary<uint, NetworkConnection> _connections = new();
  private readonly CancellationTokenSource _cts = new();
  private uint _nextId;

  /// <summary>
  /// All the currently connected clients
  /// </summary>
  public IReadOnlyDictionary<uint, NetworkConnection> Connections => _connections;

  /// <summary>
  /// Fired when a new client connects
  /// </summary>
  /// <remarks>
  /// Fired before any packet is received from the client
  /// </remarks>
  public event Action<NetworkConnection>? OnClientConnected;

  /// <summary>
  /// Fired when a client disconnects
  /// </summary>
  public event Action<NetworkConnection>? OnClientDisconnected;

  /// <summary>
  /// Fired for every packet received from any client
  /// </summary>
  public event Func<NetworkConnection, IPacket, Task>? OnPacketReceived;

  /// <summary>
  /// Listens to incoming connections
  /// </summary>
  /// <remarks>
  /// Runs until <see cref="Stop"/> gets called
  /// </remarks>
  public async Task RunAsync() {
    PacketRegistrar.RegisterAllPackets();
    _listener.Start();

    // manage the keep alive in background
    _ = KeepAliveLoopAsync(_cts.Token);

    try {
      while (!_cts.IsCancellationRequested) {
        var client = await _listener.AcceptTcpClientAsync(_cts.Token);
        var id = Interlocked.Increment(ref _nextId);

        var connection = new NetworkConnection(id, client);
        connection.OnPacketReceived += ClientPacketReceivedAsync;
        connection.OnDisconnected += ClientDisconnected;
        _connections[id] = connection;

        OnClientConnected?.Invoke(connection);

        // manage the client in background
        _ = connection.RunAsync(_cts.Token);
      }
    }
    catch (OperationCanceledException) {
      // server stopping
    }
    finally {
      _listener.Stop();

      foreach (var connection in _connections.Values) {
        connection.Close();
      }
    }
  }

  /// <summary>
  /// Stops the server and disconnects every client
  /// </summary>
  public void Stop() => _cts.Cancel();

  /// <summary>
  /// Sends a packet to a single client
  /// </summary>
  /// <remarks>
  /// If the send fails, the client gets disconnected
  /// </remarks>
  /// <param name="id">the id of the client</param>
  /// <param name="packet">the packet to send</param>
  public async Task SendToAsync(uint id, IPacket packet) {
    if (!_connections.TryGetValue(id, out var connection)) {
      return;
    }

    try {
      await connection.SendPacketAsync(packet, _cts.Token);
    }
    catch (Exception) {
      connection.Close();
    }
  }

  /// <summary>
  /// Sends a packet to every connected client
  /// </summary>
  /// <param name="packet">the packet to broadcast</param>
  public async Task BroadcastAsync(IPacket packet) {
    foreach (var id in _connections.Keys) {
      await SendToAsync(id, packet);
    }
  }

  /// <summary>
  /// Sends a packet to the given clients
  /// </summary>
  /// <param name="ids">the ids of the clients</param>
  /// <param name="packet">the packet to send</param>
  public async Task BroadcastToAsync(IEnumerable<uint> ids, IPacket packet) {
    foreach (var id in ids) {
      await SendToAsync(id, packet);
    }
  }

  private async Task ClientPacketReceivedAsync(NetworkConnection connection, IPacket packet) {
    // the keep alive response is managed here, the rest is forwarded
    if (packet is KeepAlivePacket) {
      return;
    }

    var handler = OnPacketReceived;
    if (handler != null) {
      await handler(connection, packet);
    }
  }

  private void ClientDisconnected(NetworkConnection connection) {
    _connections.TryRemove(connection.Id, out _);
    OnClientDisconnected?.Invoke(connection);
  }

  private async Task KeepAliveLoopAsync(CancellationToken ct) {
    using var timer = new PeriodicTimer(KEEP_ALIVE_INTERVAL);

    try {
      while (await timer.WaitForNextTickAsync(ct)) {
        var now = DateTime.UtcNow;

        foreach (var connection in _connections.Values) {
          // kick clients that timed out
          if (now - connection.LastReceived > TIMEOUT) {
            connection.Close();
            continue;
          }

          await SendToAsync(connection.Id, new KeepAlivePacket {
            Captcha = (uint)RandomNumberGenerator.GetInt32(int.MaxValue)
          });
        }
      }
    }
    catch (OperationCanceledException) {
      // server stopping
    }
  }

  public void Dispose() {
    Stop();
    _cts.Dispose();
    _listener.Dispose();
    GC.SuppressFinalize(this);
  }
}
