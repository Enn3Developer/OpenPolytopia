namespace OpenPolytopia.Common;

using System.Security.Cryptography;

public class LobbyManager {
  public readonly List<Lobby> Lobbies = [];

  public Lobby? this[uint id] => Lobbies.FirstOrDefault(lobby => lobby.Id == id);

  public bool AddPlayer(uint id, string name) => this[id]?.AddPlayer(new PlayerData { PlayerName = name }) ?? false;

  public Lobby NewLobby(uint maxPlayers) {
    var lobby = new Lobby { MaxPlayers = maxPlayers, Id = (uint)RandomNumberGenerator.GetInt32(0, short.MaxValue) };
    Lobbies.Add(lobby);
    return lobby;
  }
}
