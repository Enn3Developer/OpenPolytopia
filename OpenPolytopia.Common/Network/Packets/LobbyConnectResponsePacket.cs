namespace OpenPolytopia.Common.Network.Packets;

public class LobbyConnectResponsePacket : IPacket {
  public bool Ok;
  public void Serialize(List<byte> bytes) => Ok.Serialize(bytes);

  public void Deserialize(byte[] bytes) {
    var index = 0u;
    Ok.Deserialize(bytes, ref index);
  }
}
