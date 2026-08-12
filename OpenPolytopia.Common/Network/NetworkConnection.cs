namespace OpenPolytopia.Common.Network;

using System.IO;
using System.Net.Sockets;
using Packets;

/// <summary>
/// Wraps a connected <see cref="TcpClient"/> and provides framed packet send/receive on top of it.
/// Used both by the client (a single connection to the server) and
/// by the server (one connection per client).
/// </summary>
public class NetworkConnection(uint id, TcpClient client) : IDisposable {
  private readonly NetworkStream _stream = client.GetStream();
  private readonly SemaphoreSlim _writeLock = new(1, 1);
  private bool _closed;

  /// <summary>
  /// Id of this connection; assigned by the server
  /// </summary>
  public uint Id { get; } = id;

  /// <summary>
  /// Timestamp of the last packet received on this connection
  /// </summary>
  public DateTime LastReceived { get; private set; } = DateTime.UtcNow;

  /// <summary>
  /// Fired for every packet received on this connection
  /// </summary>
  public event Func<NetworkConnection, IPacket, Task>? OnPacketReceived;

  /// <summary>
  /// Fired once when the connection gets closed for any reason
  /// </summary>
  public event Action<NetworkConnection>? OnDisconnected;

  /// <summary>
  /// true while the underlying socket is connected
  /// </summary>
  public bool Connected => !_closed && client.Connected;

  /// <summary>
  /// Sends a single packet; thread-safe
  /// </summary>
  /// <param name="packet">the packet to send</param>
  /// <param name="ct">cancellation token</param>
  public async Task SendPacketAsync(IPacket packet, CancellationToken ct = default) {
    List<byte> bytes = [];
    PacketProtocol.FramePacket(packet, bytes);

    await _writeLock.WaitAsync(ct);
    try {
      await _stream.WriteAsync(bytes.ToArray(), ct);
    }
    finally {
      _writeLock.Release();
    }
  }

  /// <summary>
  /// Reads packets from the connection until it gets closed or the token gets cancelled,
  /// firing <see cref="OnPacketReceived"/> for each one.
  /// Always fires <see cref="OnDisconnected"/> at the end
  /// </summary>
  /// <param name="ct">cancellation token</param>
  public async Task RunAsync(CancellationToken ct = default) {
    try {
      while (!ct.IsCancellationRequested) {
        var packet = await PacketProtocol.ReadPacketAsync(_stream, ct);
        LastReceived = DateTime.UtcNow;

        // unknown packet, skip it
        if (packet == null) {
          continue;
        }

        var handler = OnPacketReceived;
        if (handler != null) {
          await handler(this, packet);
        }
      }
    }
    catch (Exception e) when (e is OperationCanceledException or EndOfStreamException or IOException
                                or ProtocolViolationException or ObjectDisposedException or SocketException) {
      // connection closed or unusable, fall through to cleanup
    }
    finally {
      Close();
    }
  }

  /// <summary>
  /// Closes the connection; it is safe to call this multiple times
  /// </summary>
  public void Close() {
    if (_closed) {
      return;
    }

    _closed = true;
    client.Close();
    OnDisconnected?.Invoke(this);
  }

  public void Dispose() {
    Close();
    _writeLock.Dispose();
    GC.SuppressFinalize(this);
  }
}
