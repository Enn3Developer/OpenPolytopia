namespace OpenPolytopia.Common.Network.Packets;

public class HandshakePacket : IPacket {
  public string Version = "";

  public void Serialize(List<byte> bytes) => Version.Serialize(bytes);

  public void Deserialize(byte[] bytes) {
    var index = 0u;
    Version = Version.Deserialize(bytes, ref index);
  }
}
