namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Result of the lobbies query
/// </summary>
/// <seealso cref="GetLobbiesPacket"/>
public class GetLobbiesResponsePacket : IPacket {
  public static GetLobbiesResponsePacket Default() => new() { Lobbies = [] };

  public required List<Lobby> Lobbies;

  public void Serialize(List<byte> bytes) => Lobbies.Serialize(bytes);

  public void Deserialize(byte[] bytes) {
    var index = 0u;
    Lobbies.Deserialize(bytes, ref index);
  }
}
