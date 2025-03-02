namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Ask for lobbies on the server
/// </summary>
public class GetLobbiesPacket : IPacket {
  public void Serialize(List<byte> bytes) { }

  public void Deserialize(byte[] bytes) { }
}
