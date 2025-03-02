namespace OpenPolytopia.Common.Network;

using System.Net.Sockets;
using DotNext.Threading;
using Packets;

public class ServerConnection(int port) {
  private readonly TcpListener _tcpListener = TcpListener.Create(port);
  private readonly List<Task> _clientTasks = [];
  private Atomic.Boolean _run = new(true);

  /// <summary>
  /// Starts the server
  /// </summary>
  public void Start() => _tcpListener.Start();

  /// <summary>
  /// Stops the server
  /// </summary>
  public void Stop() {
    // Stop the listener, we won't accept incoming requests
    _tcpListener.Stop();
    // Stop all the running tasks
    _run.SetAndGet(false);

    // Wait for all tasks to complete
    foreach (var task in _clientTasks) {
      task.Wait();
    }

    // Dispose of the tasks
    RemoveCompletedTasks();
  }

  /// <summary>
  /// Listens to incoming connections
  /// </summary>
  /// <remarks>
  /// Remember to call <see cref="ServerConnection.Start"/> before listening to connections
  /// </remarks>
  public async Task ListenAsync() {
    var client = await _tcpListener.AcceptTcpClientAsync();
    var task = ManageClientAsync(client);
    task.Start();
    _clientTasks.Add(task);
  }

  /// <summary>
  /// Removes all the tasks where the connection was closed
  /// </summary>
  public void Update() {
    RemoveCompletedTasks();
  }

  private void RemoveCompletedTasks() => _clientTasks.RemoveAll(task => task.IsCompleted);

  private async Task ManageClientAsync(TcpClient client) {
    // Get the stream for the client
    await using var stream = client.GetStream();
    // Prepare data
    List<byte> responseBytes = [];
    List<byte> bytes = [];

    // Loop while the server's running
    while (_run.GetAndUpdate(b => b)) {
      // Prepare data for incoming packet
      var newPacket = true;
      var contentLength = 0u;

      // Read incoming packet if data available or while missing bytes
      while (stream.DataAvailable || (contentLength != 0u && bytes.Count < contentLength)) {
        // Read data from stream
        var bufferBytes = new byte[256];
        var read = await stream.ReadAsync(bufferBytes.AsMemory());

        // Offset of where to start reading in the buffer
        var offset = 0;

        // If this is a new packet, read the content length of the packet
        if (newPacket) {
          newPacket = false;
          var indexContentLength = 0u;
          contentLength.Deserialize(bufferBytes, ref indexContentLength);
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
      var packet = (IPacket?)packetType.GetMethod("Default")?.Invoke(null, null);

      // if packet wasn't registered, skip this packet
      if (packet == null) {
        continue;
      }

      // else, deserialize it and manage it
      packet.Deserialize(contentBytes);
      if (await ManagePacket(packet, stream, responseBytes)) {
        break;
      }
    }

    // Connection should be closed now
    client.Close();
  }

  public async Task<bool> ManagePacket(IPacket packet, NetworkStream stream, List<byte> responseBytes) {
    // if the packet is a handshake, we should close the connection only if the version is invalid
    if (packet is HandshakePacket handshakePacket) {
      var response = new HandshakeResponsePacket { Ok = handshakePacket.Version == "0.1.0" };
      response.Serialize(responseBytes);
      await stream.WriteAsync(responseBytes.ToArray());
      return !response.Ok;
    }

    // Default: don't close the connection
    return false;
  }
}
