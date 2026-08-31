namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Sent periodically by the server to check if a client is still alive
/// </summary>
/// <remarks>
/// The client echoes it back; a client that doesn't send anything for too long gets disconnected
/// </remarks>
public class KeepAlivePacket : IPacket {
  public void Serialize(List<byte> bytes) {
  }

  public void Deserialize(byte[] bytes, ref uint index) {
  }
}
