namespace OpenPolytopia.Common.Network;

using System.IO;
using Packets;

/// <summary>
/// Thrown when a packet violates the wire format, like being malformed or too big
/// </summary>
public class ProtocolViolationException(string message) : Exception(message);

/// <summary>
/// Implements the wire format of the protocol
/// </summary>
/// <remarks>
/// Every packet is framed as <c>[uint content length][uint packet id][payload]</c>,
/// with every integer in network byte order (big-endian);
/// the content length covers the packet id and the payload
/// </remarks>
public static class PacketProtocol {
  /// <summary>
  /// Frames a packet into a byte list ready to be sent on the wire
  /// </summary>
  /// <param name="packet">the packet to frame</param>
  /// <param name="bytes">the list where to append the framed packet</param>
  /// <exception cref="ProtocolViolationException">if the framed packet exceeds <see cref="NetworkConstants.MAX_PACKET_SIZE"/></exception>
  public static void FramePacket(IPacket packet, List<byte> bytes) {
    // remember where this packet starts to insert the content length later
    var startIndex = bytes.Count;

    // serialize the id and the payload
    PacketRegistrar.GetPacketId(packet).Serialize(bytes);
    packet.Serialize(bytes);

    // fail on the sender instead of disconnecting the receivers
    var contentLength = (uint)(bytes.Count - startIndex);
    if (contentLength > NetworkConstants.MAX_PACKET_SIZE) {
      bytes.RemoveRange(startIndex, bytes.Count - startIndex);
      throw new ProtocolViolationException($"Packet too big: {contentLength} bytes");
    }

    // insert the content length before the id
    bytes.InsertRange(startIndex, contentLength.Serialize());
  }

  /// <summary>
  /// Frames a packet into a byte array ready to be sent on the wire
  /// </summary>
  /// <param name="packet">the packet to frame</param>
  /// <returns>the framed packet</returns>
  public static byte[] FramePacket(IPacket packet) {
    List<byte> bytes = [];
    FramePacket(packet, bytes);
    return [.. bytes];
  }

  /// <summary>
  /// Reads exactly one packet from the stream
  /// </summary>
  /// <remarks>
  /// Blocks until a full packet is available or the stream gets closed
  /// </remarks>
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

    // skip unknown packets so older clients can talk to newer servers
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
