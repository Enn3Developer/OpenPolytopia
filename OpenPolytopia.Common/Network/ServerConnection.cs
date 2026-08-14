namespace OpenPolytopia.Common.Network;

using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;
using Packets;

/// <summary>
/// Accepts TCP connections and manages one <see cref="NetworkConnection"/> per client
/// </summary>
/// <remarks>
/// Outgoing packets go through one queue per client, so they get delivered
/// in the order they were enqueued and a slow client can't delay the others.
/// Every <see cref="KEEP_ALIVE_INTERVAL"/> it sends a <see cref="KeepAlivePacket"/> to every client
/// and disconnects the ones that didn't send anything back for longer than <see cref="TIMEOUT"/>;
/// clients that don't complete a handshake within <see cref="HANDSHAKE_TIMEOUT"/> get disconnected too
/// </remarks>
/// <param name="port">the port to listen on</param>
/// <param name="bindAddress">the ip address to bind to; null to listen on every interface</param>
public class ServerConnection(int port, string? bindAddress = null) : IDisposable {
  private static readonly TimeSpan KEEP_ALIVE_INTERVAL = TimeSpan.FromSeconds(10);
  private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(30);
  private static readonly TimeSpan HANDSHAKE_TIMEOUT = TimeSpan.FromSeconds(10);
  private static readonly TimeSpan SEND_TIMEOUT = TimeSpan.FromSeconds(10);

  private readonly TcpListener _listener = bindAddress == null
    ? TcpListener.Create(port)
    : new TcpListener(System.Net.IPAddress.Parse(bindAddress), port);
  private readonly ConcurrentDictionary<uint, Client> _clients = new();
  private readonly CancellationTokenSource _cts = new();
  private uint _nextId;

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
        TcpClient tcpClient;
        try {
          tcpClient = await _listener.AcceptTcpClientAsync(_cts.Token);
        }
        catch (SocketException e) {
          // transient failure, like a connection aborted mid-handshake; keep accepting the others
          Console.Error.WriteLine($"Failed to accept a connection: {e.Message}");
          continue;
        }

        var id = Interlocked.Increment(ref _nextId);

        var connection = new NetworkConnection(id, tcpClient);
        connection.OnPacketReceived += ClientPacketReceivedAsync;
        connection.OnDisconnected += ClientDisconnected;

        var client = new Client(connection);
        _clients[id] = client;

        OnClientConnected?.Invoke(connection);

        // manage the client in background
        _ = connection.RunAsync(_cts.Token);
        _ = SenderLoopAsync(client, _cts.Token);
      }
    }
    catch (OperationCanceledException) {
      // server stopping
    }
    finally {
      _listener.Stop();

      foreach (var client in _clients.Values) {
        client.Connection.Close();
      }
    }
  }

  /// <summary>
  /// Stops the server and disconnects every client
  /// </summary>
  public void Stop() => _cts.Cancel();

  /// <summary>
  /// Marks a client as having completed the handshake
  /// </summary>
  /// <remarks>
  /// Only handshaked clients receive broadcasts and keep alive packets;
  /// the others get disconnected after <see cref="HANDSHAKE_TIMEOUT"/>
  /// </remarks>
  /// <param name="id">the id of the client</param>
  public void CompleteHandshake(uint id) {
    if (_clients.TryGetValue(id, out var client)) {
      client.HandshakeDone = true;
    }
  }

  /// <summary>
  /// Checks if a client completed the handshake
  /// </summary>
  /// <param name="id">the id of the client</param>
  public bool IsHandshakeDone(uint id) => _clients.TryGetValue(id, out var client) && client.HandshakeDone;

  /// <summary>
  /// Disconnects a client after delivering the packets already queued for him
  /// </summary>
  /// <param name="id">the id of the client</param>
  public void Kick(uint id) {
    if (_clients.TryGetValue(id, out var client)) {
      client.Outgoing.Writer.TryComplete();
    }
  }

  /// <summary>
  /// Queues a packet for a single client
  /// </summary>
  /// <param name="id">the id of the client</param>
  /// <param name="packet">the packet to send</param>
  public void SendTo(uint id, IPacket packet) => SendTo(id, PacketProtocol.FramePacket(packet));

  /// <summary>
  /// Queues an already framed packet for a single client
  /// </summary>
  /// <param name="id">the id of the client</param>
  /// <param name="frame">the framed packet to send</param>
  public void SendTo(uint id, byte[] frame) {
    if (_clients.TryGetValue(id, out var client)) {
      client.Outgoing.Writer.TryWrite(frame);
    }
  }

  /// <summary>
  /// Queues a packet for every handshaked client
  /// </summary>
  /// <param name="packet">the packet to broadcast</param>
  public void Broadcast(IPacket packet) => Broadcast(PacketProtocol.FramePacket(packet));

  /// <summary>
  /// Queues an already framed packet for every handshaked client
  /// </summary>
  /// <param name="frame">the framed packet to broadcast</param>
  public void Broadcast(byte[] frame) {
    foreach (var client in _clients.Values) {
      if (client.HandshakeDone) {
        client.Outgoing.Writer.TryWrite(frame);
      }
    }
  }

  /// <summary>
  /// Queues a packet for the given clients
  /// </summary>
  /// <param name="ids">the ids of the clients</param>
  /// <param name="packet">the packet to send</param>
  public void BroadcastTo(IEnumerable<uint> ids, IPacket packet) {
    var frame = PacketProtocol.FramePacket(packet);
    foreach (var id in ids) {
      SendTo(id, frame);
    }
  }

  /// <summary>
  /// Sends the queued packets of a client one at a time, in order
  /// </summary>
  /// <remarks>
  /// If a send fails or takes longer than <see cref="SEND_TIMEOUT"/>, the client gets disconnected
  /// </remarks>
  private static async Task SenderLoopAsync(Client client, CancellationToken ct) {
    try {
      await foreach (var frame in client.Outgoing.Reader.ReadAllAsync(ct)) {
        // timeout the send so a client that stopped reading can't pile up frames forever
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(SEND_TIMEOUT);
        await client.Connection.SendFrameAsync(frame, cts.Token);
      }
    }
    catch (Exception) {
      // send failure or server stopping, close below
    }

    // the queue only completes on a kick, disconnect once it's drained
    client.Connection.Close();
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
    if (_clients.TryRemove(connection.Id, out var client)) {
      // stop the sender loop
      client.Outgoing.Writer.TryComplete();
    }

    OnClientDisconnected?.Invoke(connection);
  }

  private async Task KeepAliveLoopAsync(CancellationToken ct) {
    using var timer = new PeriodicTimer(KEEP_ALIVE_INTERVAL);

    try {
      var keepAlive = PacketProtocol.FramePacket(new KeepAlivePacket());

      while (await timer.WaitForNextTickAsync(ct)) {
        var now = DateTime.UtcNow;

        foreach (var client in _clients.Values) {
          // kick clients that timed out
          if (now - client.Connection.LastReceived > TIMEOUT) {
            client.Connection.Close();
            continue;
          }

          // kick clients that connected but never completed a handshake
          if (!client.HandshakeDone) {
            if (now - client.ConnectedAt > HANDSHAKE_TIMEOUT) {
              client.Connection.Close();
            }

            continue;
          }

          SendTo(client.Connection.Id, keepAlive);
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

  /// <summary>
  /// Server-side state of a connected client
  /// </summary>
  private sealed class Client(NetworkConnection connection) {
    public NetworkConnection Connection { get; } = connection;
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    public volatile bool HandshakeDone;

    /// <summary>
    /// Packets waiting to be sent to this client
    /// </summary>
    public Channel<byte[]> Outgoing { get; } =
      Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
  }
}
