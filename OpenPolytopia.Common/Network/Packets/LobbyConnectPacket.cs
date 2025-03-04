namespace OpenPolytopia.Common.Network.Packets;

public class LobbyConnectPacket : IPacket {
  public uint Id;
  public void Serialize(List<byte> bytes) => Id.Serialize(bytes);

  public void Deserialize(byte[] bytes) {
    var index = 0u;
    Id.Deserialize(bytes, ref index);
  }
}
