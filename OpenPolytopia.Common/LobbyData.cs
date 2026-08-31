namespace OpenPolytopia.Common;

using Network;

/// <summary>
/// A player inside a lobby
/// </summary>
public class LobbyPlayerData : INetworkSerializable {
  /// <summary>
  /// Server-assigned id of the player
  /// </summary>
  public uint PlayerId;

  /// <summary>
  /// Name of the player
  /// </summary>
  public string Name = "";

  /// <summary>
  /// Tribe chosen by the player
  /// </summary>
  public uint Tribe;

  /// <summary>
  /// Whether the player is ready to start the game
  /// </summary>
  public bool Ready;

  public void Serialize(List<byte> bytes) {
    PlayerId.Serialize(bytes);
    Name.Serialize(bytes);
    Tribe.Serialize(bytes);
    Ready.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    PlayerId.Deserialize(bytes, ref index);
    Name = StringSerialization.Read(bytes, ref index);
    Tribe.Deserialize(bytes, ref index);
    Ready.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Represents a lobby where players can join and start a game
/// </summary>
public class LobbyData : INetworkSerializable {
  /// <summary>
  /// ID of the lobby
  /// </summary>
  public ulong Id;

  /// <summary>
  /// Number of max players that can join this lobby
  /// </summary>
  public uint MaxPlayers;

  /// <summary>
  /// If the game in the lobby has started
  /// </summary>
  public bool Started;

  /// <summary>
  /// If the game in the lobby is about to start (lobby full and all players ready)
  /// </summary>
  public bool Starting;

  /// <summary>
  /// The players currently in the lobby
  /// </summary>
  public List<LobbyPlayerData> Players = [];

  /// <summary>
  /// Number of players in the lobby
  /// </summary>
  public uint PlayersCount => (uint)Players.Count;

  /// <summary>
  /// Number of players ready to start
  /// </summary>
  public uint ReadyCount => (uint)Players.Count(player => player.Ready);

  /// <summary>
  /// Returns the player data from a given player id
  /// </summary>
  /// <param name="playerId">the player's id</param>
  public LobbyPlayerData? this[uint playerId] => Players.FirstOrDefault(player => player.PlayerId == playerId);

  public void Serialize(List<byte> bytes) {
    Id.Serialize(bytes);
    MaxPlayers.Serialize(bytes);
    Started.Serialize(bytes);
    Starting.Serialize(bytes);
    Players.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    Id.Deserialize(bytes, ref index);
    MaxPlayers.Deserialize(bytes, ref index);
    Started.Deserialize(bytes, ref index);
    Starting.Deserialize(bytes, ref index);
    Players.Deserialize(bytes, ref index);
  }
}
