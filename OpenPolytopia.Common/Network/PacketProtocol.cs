namespace OpenPolytopia.Common.Network;

using System.IO;
using Packets;

/// <summary>
/// Thrown when the remote endpoint sends a malformed or too big packet
/// </summary>
public class ProtocolViolationException(string message) : Exception(message);

/// <summary>
/// Implements the wire format of the protocol.
/// <br/>
/// Every packet is framed as <c>[uint content length][uint packet id][payload]</c>,
/// with every integer in network byte order (big-endian);
/// the content length covers the packet id and the payload.
/// </summary>
public static class PacketProtocol {
  /// <summary>
  /// Frames a packet into a byte list ready to be sent on the wire
  /// </summary>
  /// <param name="packet">the packet to frame</param>
  /// <param name="bytes">the list where to append the framed packet</param>
  public static void FramePacket(IPacket packet, List<byte> bytes) {
    // remember where this packet starts to insert the content length later
    var startIndex = bytes.Count;

    // serialize the id and the payload
    PacketRegistrar.GetPacketId(packet).Serialize(bytes);
    packet.Serialize(bytes);

    // insert the content length before the id
    var contentLength = (uint)(bytes.Count - startIndex);
    bytes.InsertRange(startIndex, contentLength.Serialize());
  }

  /// <summary>
  /// Frames a packet and writes it to the stream
  /// </summary>
  /// <param name="stream">the stream to write to</param>
  /// <param name="packet">the packet to send</param>
  /// <param name="ct">cancellation token</param>
  public static async Task WritePacketAsync(Stream stream, IPacket packet, CancellationToken ct = default) {
    List<byte> bytes = [];
    FramePacket(packet, bytes);
    await stream.WriteAsync(bytes.ToArray(), ct);
  }

  /// <summary>
  /// Reads exactly one packet from the stream.
  /// Blocks until a full packet is available or the stream gets closed
  /// </summary>
  /// <param name="stream">the stream to read from</param>
  /// <param name="ct">cancellation token</param>
  /// <returns>the packet or null if the packet id isn't registered</returns>
  /// <exception cref="ProtocolViolationException">if the remote endpoint violates the protocol</exception>
  /// <exception cref="EndOfStreamException">if the connection gets closed</exception>
  public static async Task<IPacket?> ReadPacketAsync(Stream stream, CancellationToken ct = default) {
    // read the content length
    var header = new byte[4];
    await stream.ReadExactlyAsync(header, ct);
    var index = 0u;
    var contentLength = UIntSerialization.Read(header, ref index);

    // a packet must contain at least the packet id
    if (contentLength is < 4 or > NetworkConstants.MAX_PACKET_SIZE) {
      throw new ProtocolViolationException($"Invalid packet length: {contentLength}");
    }

    // read the whole content (packet id + payload)
    var content = new byte[contentLength];
    await stream.ReadExactlyAsync(content, ct);

    // parse the packet id and create the corresponding packet
    index = 0u;
    var packetId = UIntSerialization.Read(content, ref index);
    var packet = PacketRegistrar.CreatePacket(packetId);

    // unknown packets are skipped instead of closing the connection
    // so older clients can talk to newer servers
    if (packet == null) {
      return null;
    }

    try {
      packet.Deserialize(content, ref index);
    }
    catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException) {
      throw new ProtocolViolationException($"Malformed payload for packet {packet.GetType().Name}");
    }

    return packet;
  }
}
