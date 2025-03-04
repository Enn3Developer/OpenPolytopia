namespace OpenPolytopia.Common;

using System.Security.Cryptography;

public class LobbyManager {
  public readonly List<Lobby> Lobbies = [];

  public Lobby? this[uint id] => Lobbies.FirstOrDefault(lobby => lobby.Id == id);

  public bool AddPlayer(uint id, string name) => this[id]?.AddPlayer(new PlayerData { PlayerName = name }) ?? false;

  public void RemovePlayer(uint id, string name) => this[id]?.RemovePlayer(name);

  public Lobby NewLobby(uint maxPlayers) {
    var lobby = new Lobby { MaxPlayers = maxPlayers, Id = (uint)RandomNumberGenerator.GetInt32(0, short.MaxValue) };
    Lobbies.Add(lobby);
    return lobby;
  }

  public bool CheckPlayer(string name) =>
    Lobbies.SelectMany(lobby => lobby.Players).Any(player => player.PlayerName == name);
}
