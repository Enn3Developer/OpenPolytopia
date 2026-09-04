namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Snapshot of a single player's public game state, sent as part of <see cref="GameUpdate"/> and
/// <see cref="GameOverPacket"/>
/// </summary>
public class GamePlayerData : INetworkSerializable {
  /// <summary>
  /// Id of the player (1..16)
  /// </summary>
  public uint PlayerId;

  /// <summary>
  /// Name of the player
  /// </summary>
  public string Name = "";

  /// <summary>
  /// Tribe played by this player
  /// </summary>
  public uint Tribe;

  /// <summary>
  /// Stars currently owned by the player
  /// </summary>
  public int Stars;

  /// <summary>
  /// Current score of the player
  /// </summary>
  public int Score;

  /// <summary>
  /// Whether the player is still alive
  /// </summary>
  public bool Alive;

  /// <summary>
  /// Ids of every tech node the player has researched
  /// </summary>
  public List<string> ResearchedTechs = [];

  public void Serialize(List<byte> bytes) {
    PlayerId.Serialize(bytes);
    Name.Serialize(bytes);
    Tribe.Serialize(bytes);
    Stars.Serialize(bytes);
    Score.Serialize(bytes);
    Alive.Serialize(bytes);
    ResearchedTechs.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    PlayerId.Deserialize(bytes, ref index);
    Name = StringSerialization.Read(bytes, ref index);
    Tribe.Deserialize(bytes, ref index);
    Stars.Deserialize(bytes, ref index);
    Score.Deserialize(bytes, ref index);
    Alive.Deserialize(bytes, ref index);
    ResearchedTechs.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// A single grid tile that changed since the previous <see cref="GameUpdate"/> of a game
/// </summary>
public class TileUpdate : INetworkSerializable {
  /// <summary>
  /// Index of the tile in the grid
  /// </summary>
  public uint Index;

  /// <summary>
  /// The tile's packed representation, i.e. <c>Tile.Raw</c>
  /// </summary>
  public ulong Tile;

  /// <summary>
  /// The troop's packed representation on this tile, i.e. <c>TroopData.Raw</c>; 0 means no troop
  /// </summary>
  public uint Troop;

  public void Serialize(List<byte> bytes) {
    Index.Serialize(bytes);
    Tile.Serialize(bytes);
    Troop.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    Index.Deserialize(bytes, ref index);
    Tile.Deserialize(bytes, ref index);
    Troop.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Incremental update of a game's state, carried by every gameplay broadcast packet
/// </summary>
public class GameUpdate : INetworkSerializable {
  /// <summary>
  /// Only the tiles that changed since the previous broadcast of this game; readers should merge these into
  /// their local copy of the grid rather than replace it
  /// </summary>
  public List<TileUpdate> Tiles = [];

  /// <summary>
  /// The up to date public state of every player in the game
  /// </summary>
  public List<GamePlayerData> Players = [];

  public void Serialize(List<byte> bytes) {
    Tiles.Serialize(bytes);
    Players.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    Tiles.Deserialize(bytes, ref index);
    Players.Deserialize(bytes, ref index);
  }
}
