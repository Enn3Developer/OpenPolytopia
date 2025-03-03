namespace OpenPolytopia.Common.Network.Packets;

public class CreateLobbyPacket : IPacket {
  public uint MaxPlayers;

  public void Serialize(List<byte> bytes) => MaxPlayers.Serialize(bytes);

  public void Deserialize(byte[] bytes) {
    var index = 0u;
    MaxPlayers.Deserialize(bytes, ref index);
  }
}
