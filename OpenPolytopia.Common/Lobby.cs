namespace OpenPolytopia.Common;

using System.Collections.ObjectModel;
using Network;
using Network.Packets;

/// <summary>
/// Represents a lobby
/// </summary>
public class Lobby : INetworkSerializable {
  private readonly List<PlayerData> _players = [];

  /// <summary>
  /// ID of the lobby
  /// </summary>
  public uint Id;

  /// <summary>
  /// Returns all the players in the lobby as a read-only list
  /// </summary>
  public ReadOnlyCollection<PlayerData> Players => _players.AsReadOnly();

  /// <summary>
  /// Returns the player data from a given name
  /// </summary>
  /// <param name="name">the player's name</param>
  public PlayerData? this[string name] => _players.FirstOrDefault(player => player.PlayerName == name);

  /// <summary>
  /// Adds a player to the lobby
  /// </summary>
  /// <param name="player">the player to add</param>
  public void AddPlayer(PlayerData player) => _players.Add(player);

  public void Serialize(List<byte> bytes) {
    Id.Serialize(bytes);
    _players.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    Id.Deserialize(bytes, ref index);
    _players.Deserialize(bytes, ref index);
  }
}
