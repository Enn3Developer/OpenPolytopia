namespace OpenPolytopia.Common.Network.Packets;

public class CreateLobbyResponsePacket : IPacket {
  public bool Ok;
  public uint Id;

  public void Serialize(List<byte> bytes) {
    Ok.Serialize(bytes);
    Id.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes) {
    var index = 0u;
    Ok.Deserialize(bytes, ref index);
    Id.Deserialize(bytes, ref index);
  }
}
