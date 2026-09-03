namespace OpenPolytopia.Server;

using OpenPolytopia.Common;
using OpenPolytopia.Common.Gameplay;

/// <summary>
/// Creates and owns every running <see cref="GameSession"/> on the server
/// </summary>
/// <remarks>
/// This class isn't thread-safe, <see cref="GameServer"/> serializes every access through its own lock, exactly
/// like <see cref="LobbyManager"/>
/// </remarks>
public class GameManager {
  private readonly Dictionary<ulong, GameSession> _sessions = new();
  private readonly Dictionary<uint, ulong> _connectionToGame = new();

  /// <summary>
  /// Returns a running game given its id
  /// </summary>
  /// <param name="id">the id of the game, i.e. the id of the lobby it started from</param>
  public GameSession? this[ulong id] => _sessions.GetValueOrDefault(id);

  /// <summary>
  /// Finds the game a connection is currently playing
  /// </summary>
  /// <param name="connectionId">the connection id</param>
  /// <returns>the session; null if the connection isn't mapped to any game</returns>
  public GameSession? FindByConnection(uint connectionId) =>
    _connectionToGame.TryGetValue(connectionId, out var gameId) ? this[gameId] : null;

  /// <summary>
  /// Creates and starts a new game from a lobby that just finished waiting for players
  /// </summary>
  /// <remarks>
  /// Players get ids 1..n in lobby order; the world is generated with <see cref="TerrainGeneration"/> before the
  /// game starts
  /// </remarks>
  /// <param name="lobby">the lobby whose players are starting a game</param>
  /// <param name="data">the static gameplay data to build the game from</param>
  /// <param name="seed">the optional world generation seed, mainly for deterministic tests</param>
  /// <returns>the newly created, started and registered session</returns>
  /// <exception cref="ArgumentException">if the lobby doesn't have 2 to 16 players</exception>
  public async Task<GameSession> CreateGameAsync(LobbyData lobby, GameData data, int? seed = null) {
    ArgumentNullException.ThrowIfNull(lobby);
    ArgumentNullException.ThrowIfNull(data);

    if (lobby.Players.Count is < 2 or > 16) {
      throw new ArgumentException($"a game needs 2 to 16 players, got {lobby.Players.Count}", nameof(lobby));
    }

    var grid = new Grid(lobby.WorldSize);
    var cityManager = new CityManager(grid);
    var troopManager = new TroopManager(lobby.WorldSize);
    troopManager.RegisterTroops(data.TroopsSerializedData);

    var players = new Player[lobby.Players.Count];
    var connections = new Dictionary<int, uint>(lobby.Players.Count);
    var names = new Dictionary<int, string>(lobby.Players.Count);
    for (var index = 0; index < lobby.Players.Count; index++) {
      var lobbyPlayer = lobby.Players[index];
      var playerId = index + 1;
      players[index] = new Player((TribeType)lobbyPlayer.Tribe, playerId);
      connections[playerId] = lobbyPlayer.PlayerId;
      names[playerId] = lobbyPlayer.Name;
    }

    await new TerrainGeneration(grid, cityManager, data.Tribes, players, seed).GenerateMapAsync();

    var game = new Game(grid, cityManager, troopManager, data.Tribes, data.Buildings, data.TechTreeDefinition,
      players);
    game.Start();

    var session = new GameSession(lobby.Id, game, connections, names);
    _sessions[lobby.Id] = session;
    foreach (var connectionId in connections.Values) {
      _connectionToGame[connectionId] = lobby.Id;
    }

    Console.WriteLine($"Created game {lobby.Id} with {players.Length} players on a {lobby.WorldSize}x{lobby.WorldSize} world");

    return session;
  }

  /// <summary>
  /// Removes a game and forgets the connections of every one of its players
  /// </summary>
  /// <param name="id">the id of the game to remove</param>
  public void RemoveGame(ulong id) {
    if (!_sessions.Remove(id, out var session)) {
      return;
    }

    foreach (var connectionId in session.ConnectionIds) {
      _connectionToGame.Remove(connectionId);
    }
  }

  /// <summary>
  /// Forgets the connection of a player, so it stops resolving to the game it was playing
  /// </summary>
  /// <remarks>
  /// The game itself is left untouched: the other players may still be playing it, and the caller decides whether
  /// the player behind the connection resigns
  /// </remarks>
  /// <param name="connectionId">the connection that went away</param>
  /// <param name="playerId">the id of the player behind the connection; 0 if it wasn't in any game</param>
  /// <returns>the session the connection was playing; null if it wasn't mapped to any game</returns>
  public GameSession? RemovePlayer(uint connectionId, out int playerId) {
    playerId = 0;
    if (!_connectionToGame.Remove(connectionId, out var gameId) || this[gameId] is not { } session) {
      return null;
    }

    playerId = session.RemoveConnection(connectionId);
    return session;
  }
}
