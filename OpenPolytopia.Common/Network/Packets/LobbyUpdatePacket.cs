namespace OpenPolytopia.Common.Network.Packets;

public class LobbyUpdatePacket : IPacket {
  public Lobby Lobby = new();
  public void Serialize(List<byte> bytes) => Lobby.Serialize(bytes);

  public void Deserialize(byte[] bytes) {
    var index = 0u;
    Lobby.Deserialize(bytes, ref index);
  }
}
