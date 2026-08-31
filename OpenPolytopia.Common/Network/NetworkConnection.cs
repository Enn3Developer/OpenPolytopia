namespace OpenPolytopia.Common.Network;

using System.IO;
using System.Net.Sockets;
using Packets;

/// <summary>
/// Wraps a connected <see cref="TcpClient"/> to send and receive packets
/// </summary>
/// <remarks>
/// Used by the client for its connection to the server
/// and by the server for every connected client
/// </remarks>
public class NetworkConnection(uint id, TcpClient client) : IDisposable {
  private readonly NetworkStream _stream = client.GetStream();
  private readonly SemaphoreSlim _writeLock = new(1, 1);
  private int _closed;

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
  public bool Connected => _closed == 0 && client.Connected;

  /// <summary>
  /// Sends a packet to the remote endpoint
  /// </summary>
  /// <remarks>
  /// This method is thread-safe
  /// </remarks>
  /// <param name="packet">the packet to send</param>
  /// <param name="ct">cancellation token</param>
  public async Task SendPacketAsync(IPacket packet, CancellationToken ct = default) =>
    await SendFrameAsync(PacketProtocol.FramePacket(packet), ct);

  /// <summary>
  /// Sends an already framed packet to the remote endpoint
  /// </summary>
  /// <remarks>
  /// This method is thread-safe
  /// </remarks>
  /// <param name="frame">the framed packet to send</param>
  /// <param name="ct">cancellation token</param>
  public async Task SendFrameAsync(byte[] frame, CancellationToken ct = default) {
    await _writeLock.WaitAsync(ct);
    try {
      await _stream.WriteAsync(frame, ct);
    }
    finally {
      _writeLock.Release();
    }
  }

  /// <summary>
  /// Reads packets from the connection until it gets closed or the token gets cancelled
  /// </summary>
  /// <remarks>
  /// Fires <see cref="OnPacketReceived"/> for every packet
  /// and <see cref="OnDisconnected"/> at the end
  /// </remarks>
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
  /// Closes the connection
  /// </summary>
  /// <remarks>
  /// Calling this multiple times is safe
  /// </remarks>
  public void Close() {
    // atomic exchange so two threads closing at once fire OnDisconnected only once
    if (Interlocked.Exchange(ref _closed, 1) == 1) {
      return;
    }

    client.Close();
    OnDisconnected?.Invoke(this);
  }

  public void Dispose() {
    Close();
    _writeLock.Dispose();
    GC.SuppressFinalize(this);
  }
}
