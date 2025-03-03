namespace OpenPolytopia.Common;

public class LobbyManager {
  public List<Lobby> Lobbies = [];

  public Lobby? this[uint id] => Lobbies.FirstOrDefault(lobby => lobby.Id == id);

  public bool AddPlayer(uint id, string name) => this[id]?.AddPlayer(new PlayerData { PlayerName = name }) ?? false;
}
