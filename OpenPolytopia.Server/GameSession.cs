namespace OpenPolytopia.Server;

using System.Collections.Generic;
using System.Linq;
using OpenPolytopia.Common.Gameplay;
using OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Wraps a running <see cref="Game"/> with the server-side bookkeeping needed to talk to its clients
/// </summary>
/// <remarks>
/// Built and owned by <see cref="GameManager"/>; keeps a snapshot of the packed tile/troop representation so
/// <see cref="TakeUpdate"/> can report only what changed since the last broadcast instead of the whole grid
/// </remarks>
public class GameSession {
  private readonly Dictionary<int, uint> _connections;
  private readonly Dictionary<int, uint> _accounts;
  private readonly Dictionary<uint, int> _playerIdByConnection;
  private readonly Dictionary<int, string> _names;
  private readonly ulong[] _tileSnapshot;
  private readonly uint[] _troopSnapshot;

  /// <summary>
  /// Id of this game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong Id { get; }

  /// <summary>
  /// The game this session wraps
  /// </summary>
  public Game Game { get; }

  /// <summary>The persistent turn clock, independent of connected clients.</summary>
  public TurnClock? Clock { get; internal set; }

  /// <summary>
  /// Connection id of every player in the game, keyed by player id
  /// </summary>
  public IReadOnlyDictionary<int, uint> Connections => _connections;

  /// <summary>
  /// Connection id of every player in the game, whether or not they're still connected or alive in-game
  /// </summary>
  public IEnumerable<uint> ConnectionIds => _connections.Values;

  /// <summary>
  /// Builds a session around a freshly started game
  /// </summary>
  /// <param name="id">the id of the game, i.e. the id of the lobby it started from</param>
  /// <param name="game">the started game</param>
  /// <param name="connections">connection id of every player, keyed by player id</param>
  /// <param name="names">name of every player, keyed by player id</param>
  internal GameSession(ulong id, Game game, Dictionary<int, uint> connections, Dictionary<int, string> names) {
    Id = id;
    Game = game;
    _connections = new(connections);
    _accounts = new(connections);
    _names = names;
    _playerIdByConnection = connections.ToDictionary(pair => pair.Value, pair => pair.Key);

    var cells = (int)(game.Grid.Size * game.Grid.Size);
    _tileSnapshot = new ulong[cells];
    _troopSnapshot = new uint[cells];
    for (var index = 0u; index < cells; index++) {
      _tileSnapshot[index] = game.Grid[index].Raw;
      _troopSnapshot[index] = game.Troops[index].Raw;
    }
  }

  /// <summary>
  /// Looks up the player id of a connection in this game
  /// </summary>
  /// <param name="connectionId">the connection id</param>
  /// <returns>the player id; 0 if this connection isn't part of the game</returns>
  public int PlayerIdOf(uint connectionId) => _playerIdByConnection.GetValueOrDefault(connectionId);

  /// <summary>
  /// Forgets a connection, so the player behind it stops receiving the packets of this game
  /// </summary>
  /// <param name="connectionId">the connection that went away</param>
  /// <returns>the id of the player who was using that connection; 0 if it wasn't part of this game</returns>
  internal int RemoveConnection(uint connectionId) {
    if (!_playerIdByConnection.Remove(connectionId, out var playerId)) {
      return 0;
    }

    _connections.Remove(playerId);
    return playerId;
  }

  /// <summary>Persistent account ids keyed by in-game player id.</summary>
  public IReadOnlyDictionary<int, uint> Accounts => _accounts;

  /// <summary>Names retained independently of active connections.</summary>
  public IReadOnlyDictionary<int, string> Names => _names;

  /// <summary>Resolves durable membership without requiring an open game view.</summary>
  internal void Rename(uint accountId, string name) => _names[PlayerIdOfAccount(accountId)] = name;

  public int PlayerIdOfAccount(uint accountId) =>
    _accounts.FirstOrDefault(pair => pair.Value == accountId).Key;

  /// <summary>Attaches a member to this game's updates. Never creates a new seat.</summary>
  public bool Join(uint accountId, uint connectionId) {
    var playerId = PlayerIdOfAccount(accountId);
    if (playerId == 0) return false;
    RemoveConnection(connectionId);
    if (_connections.TryGetValue(playerId, out var oldConnection)) RemoveConnection(oldConnection);
    _connections[playerId] = connectionId;
    _playerIdByConnection[connectionId] = playerId;
    return true;
  }

  /// <summary>
  /// Diffs the current grid and troops against the last snapshot and refreshes it
  /// </summary>
  /// <remarks>
  /// Call this once per broadcast: every call moves the snapshot forward, so a tile reported here won't show up
  /// again until it changes once more
  /// </remarks>
  /// <returns>every tile that changed since the previous call, and the up to date state of every player</returns>
  public GameUpdate TakeUpdate() {
    var update = new GameUpdate();
    var cells = _tileSnapshot.Length;

    for (var index = 0u; index < cells; index++) {
      var tileRaw = Game.Grid[index].Raw;
      var troopRaw = Game.Troops[index].Raw;
      if (tileRaw == _tileSnapshot[index] && troopRaw == _troopSnapshot[index]) {
        continue;
      }

      update.Tiles.Add(new TileUpdate { Index = index, Tile = tileRaw, Troop = troopRaw });
      _tileSnapshot[index] = tileRaw;
      _troopSnapshot[index] = troopRaw;
    }

    foreach (var player in Game.Players) {
      update.Players.Add(BuildPlayerData(player));
    }

    return update;
  }

  /// <summary>
  /// Builds the full state of the game, for a client that just joined or needs to resynchronize
  /// </summary>
  /// <remarks>
  /// Unlike <see cref="TakeUpdate"/>, this doesn't touch the snapshot: it always reports every tile, so it can be
  /// called freely without affecting what future <see cref="TakeUpdate"/> calls report
  /// </remarks>
  /// <returns>the packet carrying the full state, with <see cref="GameActionResult.Ok"/></returns>
  public GameStatePacket BuildState() {
    var cells = _tileSnapshot.Length;
    var tiles = new ulong[cells];
    var troops = new uint[cells];
    for (var index = 0u; index < cells; index++) {
      tiles[index] = Game.Grid[index].Raw;
      troops[index] = Game.Troops[index].Raw;
    }

    var players = new List<GamePlayerData>(Game.Players.Count);
    foreach (var player in Game.Players) {
      players.Add(BuildPlayerData(player));
    }

    return new GameStatePacket {
      Result = GameActionResult.Ok,
      GameId = Id,
      WorldSize = Game.Grid.Size,
      Turn = Game.Turn,
      CurrentPlayer = (uint)Game.CurrentPlayer,
      MaxTurns = Game.Settings.MaxTurns,
      Over = Game.Over,
      Winner = (uint)Game.Winner,
      Players = players,
      Tiles = tiles,
      Troops = troops
    };
  }

  /// <summary>
  /// Builds the public network snapshot of a single player
  /// </summary>
  /// <param name="player">the player's in-game state</param>
  /// <returns>the player's public state, ready to send over the network</returns>
  private GamePlayerData BuildPlayerData(PlayerState player) => new() {
    PlayerId = (uint)player.Id,
    AccountId = _accounts.GetValueOrDefault(player.Id),
    Name = _names.GetValueOrDefault(player.Id, ""),
    Tribe = (uint)player.Tribe,
    Stars = player.Stars,
    Score = player.Score.ScoreValue,
    Alive = player.Alive,
    ResearchedTechs = [.. player.TechTree.ResearchedIds()]
  };
}
