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
  /// Returns the player data from a given name
  /// </summary>
  /// <param name="name">the player's name</param>
  public PlayerData? this[string name] => _players.FirstOrDefault(player => player.PlayerName == name);

  /// <summary>
  /// Adds a player to the lobby
  /// </summary>
  /// <param name="player">the player to add</param>
  public void AddPlayer(PlayerData player) => _players.Add(player);

  /// <summary>
  /// Returns all the players in the lobby
  /// </summary>
  /// <returns>the players in the lobby as a read-only list</returns>
  public ReadOnlyCollection<PlayerData> GetPlayers() => _players.AsReadOnly();

  public void Serialize(List<byte> bytes) => _players.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => _players.Deserialize(bytes, ref index);
}
