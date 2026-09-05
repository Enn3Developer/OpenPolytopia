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
  /// <summary>All retained games, including completed games.</summary>
  public IReadOnlyCollection<GameSession> Sessions => _sessions.Values;

  internal void Clear() => _sessions.Clear();
  internal void Restore(GameSession session) => _sessions.Add(session.Id, session);

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
    _sessions.Values.FirstOrDefault(session => session.PlayerIdOf(connectionId) != 0);

  /// <summary>Lists every game belonging to a persistent account.</summary>
  public IEnumerable<GameSession> FindByAccount(uint accountId) =>
    _sessions.Values.Where(session => session.PlayerIdOfAccount(accountId) != 0);

  /// <summary>Detaches a transport from every game without resigning.</summary>
  public void Disconnect(uint connectionId) {
    foreach (var session in _sessions.Values) session.RemoveConnection(connectionId);
  }

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
  public async Task<GameSession> CreateGameAsync(LobbyData lobby, GameData data, int? seed = null,
    IReadOnlyDictionary<uint, uint>? onlineConnections = null, DateTimeOffset? startedAt = null) {
    ArgumentNullException.ThrowIfNull(lobby);
    ArgumentNullException.ThrowIfNull(data);

    if (lobby.Players.Count is < 2 or > 16) {
      throw new ArgumentException($"a game needs 2 to 16 players, got {lobby.Players.Count}", nameof(lobby));
    }

    if (_sessions.ContainsKey(lobby.Id) || lobby.Players.Select(p => p.PlayerId).Distinct().Count() != lobby.Players.Count) {
      throw new InvalidOperationException("duplicate game or participant");
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

    var session = new GameSession(lobby.Id, game, connections, names) {
      Clock = new TurnClock((TurnTimerMode)lobby.TimerMode, game.Players.Select(p => p.Id))
    };
    session.Clock.Begin(game.CurrentPlayer, startedAt ?? DateTimeOffset.UtcNow);
    if (onlineConnections != null) {
      foreach (var connectionId in session.ConnectionIds.ToArray()) session.RemoveConnection(connectionId);
      foreach (var accountId in connections.Values) {
        if (onlineConnections.TryGetValue(accountId, out var connectionId)) session.Join(accountId, connectionId);
      }
    }
    _sessions[lobby.Id] = session;
    Console.WriteLine($"Created game {lobby.Id} with {players.Length} players on a {lobby.WorldSize}x{lobby.WorldSize} world");

    return session;
  }

  /// <summary>
  /// Removes a game and forgets the connections of every one of its players
  /// </summary>
  /// <param name="id">the id of the game to remove</param>
  public void RemoveGame(ulong id) => _sessions.Remove(id);

  /// <summary>Detaches a connection from the first matching session.</summary>
  public GameSession? RemovePlayer(uint connectionId, out int playerId) {
    var session = FindByConnection(connectionId);
    playerId = session?.RemoveConnection(connectionId) ?? 0;
    return session;
  }
}
