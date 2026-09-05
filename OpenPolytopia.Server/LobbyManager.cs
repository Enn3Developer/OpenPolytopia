namespace OpenPolytopia.Server;

using OpenPolytopia.Common;
using OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Owns all the lobbies on the server
/// </summary>
/// <remarks>
/// This class isn't thread-safe, <see cref="GameServer"/> serializes every access through its own lock
/// </remarks>
public class LobbyManager {
  private readonly Dictionary<ulong, LobbyData> _lobbies = new();
  private ulong _nextId;

  /// <summary>Last allocated id, retained even after the last lobby is removed.</summary>
  public ulong LastId => _nextId;

  /// <summary>Restores durable lobby state before accepting connections.</summary>
  public void Restore(ulong lastId, IEnumerable<LobbyData> lobbies) {
    _lobbies.Clear();
    _nextId = lastId;
    foreach (var lobby in lobbies) {
      if (lobby.Id == 0 || lobby.Id > lastId || !_lobbies.TryAdd(lobby.Id, lobby))
        throw new InvalidDataException("Invalid persisted lobby id");
    }
  }

  /// <summary>
  /// All the lobbies on the server
  /// </summary>
  public IReadOnlyCollection<LobbyData> Lobbies => _lobbies.Values;

  /// <summary>
  /// Number of lobbies currently on the server
  /// </summary>
  public int LobbiesCount => _lobbies.Count;

  /// <summary>
  /// Returns a lobby given its id
  /// </summary>
  /// <param name="id">the id of the lobby</param>
  public LobbyData? this[ulong id] => _lobbies.GetValueOrDefault(id);

  /// <summary>
  /// Creates a new lobby and adds the creator to it
  /// </summary>
  /// <remarks>
  /// A lobby is never created in an invalid state: the world has to be able to host every player
  /// </remarks>
  /// <param name="maxPlayers">max players that can join the lobby</param>
  /// <param name="worldSize">size of the world the game will be played on</param>
  /// <param name="creator">the player creating the lobby</param>
  /// <param name="lobby">the new lobby, or <see langword="null"/> if it couldn't be created</param>
  /// <returns>the result of the operation</returns>
  public LobbyActionResult CreateLobby(uint maxPlayers, uint worldSize, LobbyPlayerData creator,
    out LobbyData? lobby, uint timerMode = 1) {
    if (!LobbyRules.IsValidLobby(maxPlayers, worldSize) || timerMode > 1) {
      lobby = null;
      return LobbyActionResult.InvalidParameters;
    }

    lobby = new LobbyData {
      Id = ++_nextId, MaxPlayers = maxPlayers, WorldSize = worldSize, Players = [creator], TimerMode = timerMode
    };
    _lobbies[lobby.Id] = lobby;
    return LobbyActionResult.Ok;
  }

  /// <summary>
  /// Adds a player to a lobby, applying all the lobby rules
  /// </summary>
  /// <param name="lobbyId">the id of the lobby to join</param>
  /// <param name="player">the joining player</param>
  /// <returns>the result of the operation</returns>
  public LobbyActionResult JoinLobby(ulong lobbyId, LobbyPlayerData player) {
    var lobby = this[lobbyId];
    if (lobby == null) {
      return LobbyActionResult.LobbyNotFound;
    }

    if (lobby.Starting) {
      return LobbyActionResult.LobbyAlreadyStarted;
    }

    if (lobby[player.PlayerId] != null) {
      return LobbyActionResult.AlreadyJoinedLobby;
    }

    if (lobby.PlayersCount >= lobby.MaxPlayers) {
      return LobbyActionResult.LobbyFull;
    }

    lobby.Players.Add(player);
    return LobbyActionResult.Ok;
  }

  /// <summary>
  /// Removes a player from a lobby
  /// </summary>
  /// <param name="lobbyId">the id of the lobby to leave</param>
  /// <param name="playerId">the id of the leaving player</param>
  /// <returns>the result of the operation</returns>
  public LobbyActionResult LeaveLobby(ulong lobbyId, uint playerId) {
    var lobby = this[lobbyId];
    if (lobby == null) {
      return LobbyActionResult.LobbyNotFound;
    }

    if (lobby.Starting) {
      return LobbyActionResult.LobbyAlreadyStarted;
    }

    var player = lobby[playerId];
    if (player == null) {
      return LobbyActionResult.NotInLobby;
    }

    lobby.Players.Remove(player);

    // an empty lobby has nothing left to wait for
    if (lobby.PlayersCount == 0) {
      _lobbies.Remove(lobby.Id);
    }

    return LobbyActionResult.Ok;
  }

  /// <summary>
  /// Sets the ready state of a player in a lobby
  /// </summary>
  /// <remarks>
  /// When the lobby is full and every player in it is ready, the lobby is marked as starting
  /// </remarks>
  /// <param name="lobbyId">the id of the lobby</param>
  /// <param name="playerId">the id of the player</param>
  /// <param name="ready">the new ready state</param>
  /// <returns>the result of the operation</returns>
  public LobbyActionResult SetReady(ulong lobbyId, uint playerId, bool ready) {
    var lobby = this[lobbyId];
    if (lobby == null) {
      return LobbyActionResult.LobbyNotFound;
    }

    if (lobby.Starting) {
      return LobbyActionResult.LobbyAlreadyStarted;
    }

    var player = lobby[playerId];
    if (player == null) {
      return LobbyActionResult.NotInLobby;
    }

    player.Ready = ready;
    TryMarkStarting(lobby);
    return LobbyActionResult.Ok;
  }

  /// <summary>
  /// Marks a lobby as starting when it is full and every player in it is ready
  /// </summary>
  /// <remarks>
  /// Only <see cref="SetReady"/> can start a lobby: joining adds a not-ready player
  /// and leaving drops the lobby below <see cref="LobbyData.MaxPlayers"/>
  /// </remarks>
  /// <param name="lobby">the lobby to check</param>
  private static void TryMarkStarting(LobbyData lobby) {
    if (lobby.PlayersCount == lobby.MaxPlayers
        && lobby.PlayersCount >= LobbyRules.MIN_PLAYERS
        && lobby.ReadyCount == lobby.PlayersCount) {
      lobby.Starting = true;
    }
  }

  /// <summary>
  /// Checks if a player joined any lobby
  /// </summary>
  /// <param name="playerId">the id of the player</param>
  public bool IsPlayerInAnyLobby(uint playerId) => _lobbies.Values.Any(lobby => lobby[playerId] != null);

  /// <summary>
  /// Renames a player in every lobby he joined
  /// </summary>
  /// <param name="playerId">the id of the player</param>
  /// <param name="name">the new name</param>
  /// <param name="updated">filled with the lobbies that changed</param>
  public void RenamePlayerInLobbies(uint playerId, string name, List<LobbyData> updated) {
    foreach (var lobby in _lobbies.Values) {
      var player = lobby[playerId];
      if (player == null) {
        continue;
      }

      player.Name = name;
      updated.Add(lobby);
    }
  }

  /// <summary>
  /// Removes a lobby
  /// </summary>
  /// <param name="lobbyId">the id of the lobby to remove</param>
  public void RemoveLobby(ulong lobbyId) => _lobbies.Remove(lobbyId);

  /// <summary>
  /// Removes a player from every lobby he joined
  /// </summary>
  /// <remarks>
  /// Used when a client disconnects; lobbies that become empty get removed
  /// </remarks>
  /// <param name="playerId">the id of the disconnected player</param>
  /// <param name="updated">filled with the lobbies that changed</param>
  /// <param name="deleted">filled with the ids of the lobbies that got removed</param>
  public void RemovePlayerFromAllLobbies(uint playerId, List<LobbyData> updated, List<ulong> deleted) {
    foreach (var lobby in _lobbies.Values.ToArray()) {
      var player = lobby[playerId];
      if (player == null) {
        continue;
      }

      lobby.Players.Remove(player);

      if (lobby.PlayersCount == 0) {
        _lobbies.Remove(lobby.Id);
        deleted.Add(lobby.Id);
      }
      else {
        // the lobby isn't full anymore, so it stops starting
        lobby.Starting = false;
        updated.Add(lobby);
      }
    }
  }

  /// <summary>
  /// Removes and returns all the lobbies that are marked as starting
  /// </summary>
  /// <returns>the lobbies whose game must start now</returns>
  public List<LobbyData> TakeStartingLobbies() {
    List<LobbyData> starting = [];

    foreach (var lobby in _lobbies.Values.ToArray()) {
      if (!lobby.Starting) {
        continue;
      }

      // the flag marks the snapshot handed to the game; the lobby itself leaves the manager,
      // so no lobby inside it is ever started
      lobby.Started = true;
      _lobbies.Remove(lobby.Id);
      starting.Add(lobby);
    }

    return starting;
  }
}
