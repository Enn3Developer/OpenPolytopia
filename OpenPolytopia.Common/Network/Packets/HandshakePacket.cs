namespace OpenPolytopia.Common.Network.Packets;

public class HandshakePacket : IPacket {
  public static HandshakePacket Default() => new() { Version = "" };

  public required string Version;

  public void Serialize(List<byte> bytes) => Version.Serialize(bytes);

  public void Deserialize(byte[] bytes) {
    var index = 0u;
    Version.Deserialize(bytes, ref index);
  }
}
