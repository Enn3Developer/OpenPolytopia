namespace OpenPolytopia.Common.Network;

using System.Collections.Concurrent;
using System.Net.Sockets;
using DotNext.Threading;
using Packets;

public abstract class NetworkConnection {
  private Func<NetworkStream, CancellationTokenSource, uint, Task>? _callback;

  public readonly ConcurrentDictionary<uint, ConcurrentQueue<IPacket>> Channels = new();

  /// <summary>
  /// Event handler for receiving packets
  /// </summary>
  /// <remarks>
  /// uint id -> id of the client that sent the packet
  /// <br/>
  /// IPacket packet -> packet that fired this event
  /// <br/>
  /// NetworkStream stream -> stream with the client that sent this packet
  /// <br/>
  /// List&lt;byte&gt; bytes -> list to use with <see cref="NetworkStreamExtension.WritePacketAsync"/>
  /// </remarks>
  public delegate Task<bool> PacketReceived(uint id, IPacket packet, NetworkStream stream,
    List<byte> bytes);

  public delegate Task ClientDisconnected(uint id);

  /// <summary>
  /// Event fired when receiving packets
  /// </summary>
  /// <seealso cref="PacketReceived"/>
  public event PacketReceived? OnPacketReceived;

  public event ClientDisconnected? OnClientDisconnected;

  /// <summary>
  /// How to manage the receiving of the <see cref="KeepAlivePacket"/>
  /// </summary>
  /// <param name="packet">the keep alive packet</param>
  /// <param name="stream">the stream from the client that sent it</param>
  /// <param name="bytes">the bytes where to write the response, if needed</param>
  /// <returns>a completable task; use async if possible</returns>
  protected abstract Task ManageKeepAlivePacketAsync(KeepAlivePacket packet, NetworkStream stream, List<byte> bytes);

  /// <summary>
  /// Registers a new callback to use for client connections.
  /// <br/>
  /// It starts executing before trying to read the first packet from the client and passes the stream of the client
  /// and the cancellation token that <see cref="NetworkConnection.ManageClientAsync"/> uses
  /// </summary>
  /// <param name="callback">the callback function</param>
  /// <remarks>
  /// Used in <see cref="ServerConnection"/> to send <see cref="KeepAlivePacket"/>s to client and close the connection
  /// if a certain amount of time have passed without the client sending it back
  /// </remarks>
  protected void RegisterCustomStreamHandler(Func<NetworkStream, CancellationTokenSource, uint, Task> callback) =>
    _callback = callback;

  protected async Task FireClientDisconnected(uint id) {
    var result = OnClientDisconnected?.BeginInvoke(id, null, null);
    if (result == null) {
      return;
    }

    await result.AsyncWaitHandle.WaitAsync();
  }

  /// <summary>
  /// Manages a connected client
  /// </summary>
  /// <param name="id">the id of the client</param>
  /// <param name="client">the client object</param>
  /// <param name="ct">the cancellation token</param>
  protected async Task ManageClientAsync(uint id, TcpClient client, CancellationToken ct) {
    // Get the stream for the client
    await using var stream = client.GetStream();

    // initialize the channel
    Channels[id] = new ConcurrentQueue<IPacket>();

    // Prepare data
    List<byte> responseBytes = [];
    List<byte> bytes = [];

    var cts = new CancellationTokenSource();
    Task? task = null;
    if (_callback != null) {
      task = _callback(stream, cts, id);
      task.Start();
    }

    try {
      // Loop while the server's running or the connection wasn't closed because of missing KeepAlivePacket
      while (!ct.IsCancellationRequested && !cts.IsCancellationRequested) {
        // Prepare data for incoming packet
        var newPacket = true;
        var contentLength = 0u;

        // Read incoming packet if data available or while missing bytes
        while (stream.DataAvailable || (contentLength != 0u && bytes.Count < contentLength)) {
          // compute buffer size to read exactly one packet
          var bufferSize = 4L;
          if (contentLength != 0u) {
            bufferSize = contentLength - bytes.Count;
          }

          // Read data from stream
          var bufferBytes = new byte[bufferSize];
          var read = await stream.ReadAsync(bufferBytes.AsMemory(), ct);

          // Offset of where to start reading in the buffer
          var offset = 0;

          // If this is a new packet, read the content length of the packet
          if (newPacket) {
            newPacket = false;
            var indexContentLength = 0u;
            contentLength = UIntExtension.Deserialize(contentLength, bufferBytes, ref indexContentLength);
            // Set offset to 4 because we read an uint
            offset += 4;
          }

          // Copy the buffer over to our list starting from the offset
          for (var i = offset; i < read; i++) {
            bytes.Add(bufferBytes[i]);
          }
        }

        // Takes the first four bytes and parse the packet id
        var packetIdBytes = new[] { bytes[0], bytes[1], bytes[2], bytes[3] };
        bytes.RemoveRange(0, 4);
        var indexPacketId = 0u;
        var packetId = 0u;
        packetId.Deserialize(packetIdBytes, ref indexPacketId);

        // Now we get the packet type from the registered packets and call the Default method to initialize it
        var contentBytes = bytes.ToArray();
        var packetType = PacketRegistrar.GetPacket(packetId);
        var packet = (IPacket?)Activator.CreateInstance(packetType);

        // check if channel has data
        if (!Channels[id].IsEmpty) {
          var channel = Channels[id];
          while (channel.TryDequeue(out var channelPacket)) {
            stream.PreparePacket(channelPacket, responseBytes);
          }
        }

        // if packet wasn't registered, skip this packet
        if (packet == null) {
          continue;
        }

        // else, deserialize it and manage it
        packet.Deserialize(contentBytes);
        if (await ManagePacketAsync(id, packet, stream, responseBytes, ct)) {
          break;
        }

        if (responseBytes.Count > 0) {
          await stream.SendPacketsAsync(responseBytes);
        }
      }
    }
    catch (OperationCanceledException) {
    }
    finally {
      // Check if the cancellation token of the callback wasn't already signalled, if so signal to cancel
      if (!cts.IsCancellationRequested) {
        await cts.CancelAsync();
      }

      // Check if a task was registered, if so waits for its completion
      if (task != null) {
        await task;
      }

      // remove the channel
      Channels.Remove(id, out _);

      // Connection should be closed now
      client.Close();
    }
  }

  private async Task<bool> ManagePacketAsync(uint id, IPacket packet, NetworkStream stream, List<byte> responseBytes,
    CancellationToken ct) {
    if (packet is KeepAlivePacket keepAlivePacket) {
      await ManageKeepAlivePacketAsync(keepAlivePacket, stream, responseBytes);

      return false;
    }

    var result = OnPacketReceived?.BeginInvoke(id, packet, stream, responseBytes, null, null);
    if (result == null) {
      return false;
    }

    return await result.AsyncWaitHandle.WaitAsync(TimeSpan.MaxValue, token: ct);
  }
}
