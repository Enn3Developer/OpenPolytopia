namespace OpenPolytopia.Common.Gameplay;

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Runs a single match: player state, turn order, elimination and game-over rules
/// </summary>
/// <remarks>
/// Split across multiple files (<see langword="partial"/>): this file holds the state, the constructor, turn
/// management and the helpers shared by every action; movement/combat/training, research and building/capture are
/// added by later files in the same class
/// </remarks>
public partial class Game {
  private readonly Dictionary<int, PlayerState> _playersById;
  private readonly Dictionary<int, int> _turnIndexById;

  /// <summary>
  /// The grid this game is played on
  /// </summary>
  public Grid Grid { get; }

  /// <summary>
  /// The cities registered on <see cref="Grid"/>
  /// </summary>
  public CityManager Cities { get; }

  /// <summary>
  /// The troops on <see cref="Grid"/>
  /// </summary>
  public TroopManager Troops { get; }

  /// <summary>
  /// The registered building definitions
  /// </summary>
  public BuildingManager Buildings { get; }

  /// <summary>
  /// The settings this game was created with
  /// </summary>
  public GameSettings Settings { get; }

  /// <summary>
  /// Every player in this game, in turn order
  /// </summary>
  public IReadOnlyList<PlayerState> Players { get; }

  /// <summary>
  /// Gets a player's state by id
  /// </summary>
  /// <param name="playerId">the player id</param>
  /// <returns>the player's state; null if no player with that id is in this game</returns>
  public PlayerState? this[int playerId] => _playersById.GetValueOrDefault(playerId);

  /// <summary>
  /// The current turn number
  /// </summary>
  /// <remarks>
  /// 0 before <see cref="Start"/> is called; the first turn is 1
  /// </remarks>
  public uint Turn { get; private set; }

  /// <summary>
  /// The id of the player whose turn it currently is
  /// </summary>
  /// <remarks>
  /// 0 before <see cref="Start"/> is called
  /// </remarks>
  public int CurrentPlayer { get; private set; }

  /// <summary>
  /// Whether <see cref="Start"/> has been called
  /// </summary>
  public bool Started { get; private set; }

  /// <summary>
  /// Whether the game has ended
  /// </summary>
  public bool Over { get; private set; }

  /// <summary>
  /// The id of the winning player; 0 if the game hasn't ended
  /// </summary>
  public int Winner { get; private set; }

  /// <summary>
  /// Creates a new game
  /// </summary>
  /// <param name="grid">the grid to play on</param>
  /// <param name="cityManager">the cities registered on <paramref name="grid"/></param>
  /// <param name="troopManager">the troop manager for <paramref name="grid"/></param>
  /// <param name="tribeManager">the registered tribes, used to look up every player's starting stars and tech tree</param>
  /// <param name="buildingManager">the registered building definitions</param>
  /// <param name="techTreeDefinition">the shape of the tech tree every player's tree is built from</param>
  /// <param name="players">the players, in turn order</param>
  /// <param name="settings">the game settings; defaults to no turn limit when omitted</param>
  /// <exception cref="ArgumentNullException">if any required argument is null</exception>
  /// <exception cref="ArgumentException">
  /// if <paramref name="players"/> doesn't have 2 to 16 entries, an id isn't between 1 and 16, an id is used more
  /// than once, or a player's tribe isn't registered in <paramref name="tribeManager"/>
  /// </exception>
  public Game(Grid grid, CityManager cityManager, TroopManager troopManager, TribeManager tribeManager,
    BuildingManager buildingManager, TechTreeDefinition techTreeDefinition, IReadOnlyList<IPlayer> players,
    GameSettings? settings = null) {
    ArgumentNullException.ThrowIfNull(grid);
    ArgumentNullException.ThrowIfNull(cityManager);
    ArgumentNullException.ThrowIfNull(troopManager);
    ArgumentNullException.ThrowIfNull(tribeManager);
    ArgumentNullException.ThrowIfNull(buildingManager);
    ArgumentNullException.ThrowIfNull(techTreeDefinition);
    ArgumentNullException.ThrowIfNull(players);

    if (players.Count is < 2 or > 16) {
      throw new ArgumentException($"a game needs 2 to 16 players, got {players.Count}", nameof(players));
    }

    var playerStates = new List<PlayerState>(players.Count);
    var playersById = new Dictionary<int, PlayerState>(players.Count);
    var turnIndexById = new Dictionary<int, int>(players.Count);

    for (var index = 0; index < players.Count; index++) {
      var player = players[index];

      if (player.Id is < 1 or > 16) {
        throw new ArgumentException($"player id {player.Id} is out of range; ids must be between 1 and 16",
          nameof(players));
      }

      if (!turnIndexById.TryAdd(player.Id, index)) {
        throw new ArgumentException($"player id {player.Id} is used by more than one player", nameof(players));
      }

      var tribe = tribeManager[player.Tribe] ??
        throw new ArgumentException($"tribe {player.Tribe} of player {player.Id} isn't registered", nameof(players));

      var playerState = new PlayerState(player.Id, player.Tribe, tribe.StartingStars,
        techTreeDefinition.CreateTechTree(tribe));
      playerStates.Add(playerState);
      playersById.Add(player.Id, playerState);
    }

    Grid = grid;
    Cities = cityManager;
    Troops = troopManager;
    Buildings = buildingManager;
    Settings = settings ?? new GameSettings();
    Players = playerStates;
    _playersById = playersById;
    _turnIndexById = turnIndexById;
  }

  /// <summary>
  /// Starts the game: spawns a warrior on every capital and begins the first player's turn
  /// </summary>
  /// <exception cref="InvalidOperationException">if the game already started</exception>
  public void Start() {
    if (Started) {
      throw new InvalidOperationException("the game already started");
    }

    Started = true;

    foreach (var player in Players) {
      // spawn on every capital owned by this player, not just the first one found
      foreach (var cityId in CityIdsOf(player.Id).Where(id => Cities[id].Capital)) {
        var position = Grid.IndexToGridPosition(Cities.GetIndex(cityId));
        Troops.SpawnTroop(position, (uint)player.Id, cityId, TroopType.Warrior);
        Cities.ModifyCity(cityId, (ref CityData city) => city.Troops++);
      }
    }

    Turn = 1;
    CurrentPlayer = Players[0].Id;
    BeginTurn(Players[0]);
  }

  /// <summary>
  /// Ends the current player's turn and passes it to the next alive player
  /// </summary>
  /// <param name="playerId">the player ending their turn</param>
  /// <returns>the result of the request; on success, the new turn state, or how the game ended if it did</returns>
  public EndTurnResult EndTurn(int playerId) {
    var result = CheckTurn(playerId, out _);
    return result == GameActionResult.Ok ? AdvanceTurn() : Rejected(result);
  }

  /// <summary>
  /// Resigns a player from the game
  /// </summary>
  /// <remarks>
  /// Unlike every other action, this isn't a turn action: any player still in the game can resign on any turn, not
  /// just the current one
  /// </remarks>
  /// <param name="playerId">the player resigning</param>
  /// <returns>
  /// the result of the request; on success, whether the resignation passed the turn (the resigning player was the
  /// current one) or left it where it was
  /// </returns>
  public EndTurnResult Resign(int playerId) {
    var result = CheckInGame(playerId, out var player);
    if (result != GameActionResult.Ok) {
      return Rejected(result);
    }

    var wasCurrentPlayer = player.Id == CurrentPlayer;
    Eliminate(player);

    if (Over) {
      return new EndTurnResult(GameActionResult.Ok, Turn, 0, true, Winner);
    }

    return wasCurrentPlayer ? AdvanceTurn() : new EndTurnResult(GameActionResult.Ok, Turn, CurrentPlayer, false, 0);
  }

  /// <summary>
  /// Computes a player's income: the stars produced by every city they own
  /// </summary>
  /// <param name="playerId">the player id</param>
  /// <returns>the sum of <see cref="CityData.Stars"/> over every city owned by the player</returns>
  public int Income(int playerId) => CityIdsOf(playerId).Sum(cityId => Cities[cityId].Stars);

  /// <summary>
  /// Counts how many cities a player owns
  /// </summary>
  /// <param name="playerId">the player id</param>
  /// <returns>the number of cities owned by the player</returns>
  public uint OwnedCities(int playerId) => (uint)CityIdsOf(playerId).Count();

  /// <summary>
  /// Lists the ids of every city a player owns
  /// </summary>
  /// <param name="playerId">the player id</param>
  /// <returns>the ids of the cities whose <see cref="CityData.Owner"/> is <paramref name="playerId"/></returns>
  public IEnumerable<uint> CityIdsOf(int playerId) {
    for (var index = 0; index < Cities.Cities.Count; index++) {
      var cityId = (uint)(index + 1);
      if (Cities[cityId].Owner == playerId) {
        yield return cityId;
      }
    }
  }

  /// <summary>
  /// Checks whether a player can act on their turn right now
  /// </summary>
  /// <param name="playerId">the player id</param>
  /// <param name="player">the player's state; only valid when the result is <see cref="GameActionResult.Ok"/></param>
  /// <returns>
  /// <see cref="GameActionResult.GameOver"/> if the game hasn't started or already ended,
  /// <see cref="GameActionResult.NotInGame"/> if no such player exists,
  /// <see cref="GameActionResult.PlayerEliminated"/> if they've been eliminated,
  /// <see cref="GameActionResult.NotYourTurn"/> if it isn't their turn, <see cref="GameActionResult.Ok"/> otherwise
  /// </returns>
  internal GameActionResult CheckTurn(int playerId, out PlayerState player) {
    var result = CheckInGame(playerId, out player);
    if (result != GameActionResult.Ok) {
      return result;
    }

    return playerId == CurrentPlayer ? GameActionResult.Ok : GameActionResult.NotYourTurn;
  }

  /// <summary>
  /// Whether a grid position is inside the bounds of <see cref="Grid"/>
  /// </summary>
  /// <param name="position">the position to check</param>
  /// <returns>true if both coordinates are within <c>[0, Grid.Size)</c></returns>
  internal bool IsInside(Vector2I position) =>
    position.X >= 0 && position.X < (int)Grid.Size && position.Y >= 0 && position.Y < (int)Grid.Size;

  /// <summary>
  /// Kills a troop: removes it, decrements its city's troop count and scores the loss to its owner
  /// </summary>
  /// <param name="index">the grid index of the troop</param>
  internal void KillTroop(uint index) {
    var troop = Troops[index];
    if (!troop.IsValid()) {
      return;
    }

    if (troop.City != 0) {
      Cities.ModifyCity(troop.City, (ref CityData city) => city.Troops = Math.Max(city.Troops - 1, 0));
    }

    if (this[(int)troop.Player] is { } owner) {
      owner.Score.AddScore(ScoreType.LoseTroop(Troops[troop.Type].Cost));
    }

    Troops.DeleteTroop(Grid.IndexToGridPosition(index));
  }

  /// <summary>
  /// Eliminates a player from the game: kills every one of their troops and checks whether the game is over
  /// </summary>
  /// <remarks>
  /// The troops go through <see cref="KillTroop"/> so the losses are scored and the troop counts of their cities
  /// stay right
  /// </remarks>
  /// <param name="player">the player to eliminate</param>
  internal void Eliminate(PlayerState player) {
    player.Alive = false;

    // snapshot the indexes first: deleting while enumerating the same lazy sequence is fragile to reason about
    var troopIndexes = Troops.Troops().Where(entry => entry.Troop.Player == (uint)player.Id)
      .Select(entry => entry.Index).ToList();
    foreach (var index in troopIndexes) {
      KillTroop(index);
    }

    CheckGameOver();
  }

  /// <summary>
  /// Begins a player's turn: pays their income, heals their idle troops and resets every troop's actions
  /// </summary>
  /// <param name="player">the player whose turn is beginning</param>
  private void BeginTurn(PlayerState player) {
    player.Stars += Income(player.Id);

    var ownTroops = Troops.Troops().Where(entry => entry.Troop.Player == (uint)player.Id).ToList();
    foreach (var (index, troop) in ownTroops) {
      var maxHp = MaxHpOf(troop);
      var canHeal = !troop.Moved && !troop.Attacked && troop.Hp < maxHp;

      // a troop standing on a tile it owns heals more; read before ResetActions overwrites Moved/Attacked below
      var healAmount = canHeal && Grid[index].Owner == player.Id ? 4u : 2u;

      Troops.ModifyTroop(index, (ref TroopData data) => {
        if (canHeal) {
          data.Hp = Math.Min(data.Hp + healAmount, maxHp);
        }

        data.ResetActions();
      });
    }
  }

  /// <summary>
  /// Ends the game once at most one player remains alive
  /// </summary>
  /// <remarks>
  /// Idempotent: does nothing if the game already ended, so repeated eliminations never overwrite an already
  /// decided winner
  /// </remarks>
  internal void CheckGameOver() {
    if (Over || Players.Count(p => p.Alive) > 1) {
      return;
    }

    Over = true;
    Winner = WinnerByScore();
  }

  /// <summary>
  /// Advances turn order to the next alive player, incrementing <see cref="Turn"/> when the order wraps, and ends
  /// the game with a score-based winner if that would exceed <see cref="GameSettings.MaxTurns"/>
  /// </summary>
  /// <returns>the outcome of the advance: either the next player's turn, or how the game ended</returns>
  private EndTurnResult AdvanceTurn() {
    var currentIndex = _turnIndexById[CurrentPlayer];
    var nextIndex = currentIndex;
    var wrapped = false;

    // at least one other player is alive here, otherwise the game would already be over (see CheckGameOver), so
    // this loop always finds one within a single lap
    do {
      nextIndex++;
      if (nextIndex >= Players.Count) {
        nextIndex = 0;
        wrapped = true;
      }
    } while (!Players[nextIndex].Alive);

    var newTurn = wrapped ? Turn + 1 : Turn;
    if (Settings.MaxTurns > 0 && newTurn > Settings.MaxTurns) {
      // the turn never actually begins: the game ends instead and the current player keeps holding the turn
      Over = true;
      Winner = WinnerByScore();
      return new EndTurnResult(GameActionResult.Ok, Turn, 0, true, Winner);
    }

    Turn = newTurn;
    CurrentPlayer = Players[nextIndex].Id;
    BeginTurn(Players[nextIndex]);
    return new EndTurnResult(GameActionResult.Ok, Turn, CurrentPlayer, false, 0);
  }

  /// <summary>
  /// The alive player with the highest score, ties broken by turn order
  /// </summary>
  /// <returns>the winning player's id; 0 if no player is alive</returns>
  private int WinnerByScore() {
    PlayerState? best = null;
    foreach (var player in Players) {
      // strictly greater, not >=, so the earliest player in turn order wins a tie
      if (player.Alive && (best == null || player.Score.ScoreValue > best.Score.ScoreValue)) {
        best = player;
      }
    }

    return best?.Id ?? 0;
  }

  /// <summary>
  /// Checks whether a player is a live participant of this game, regardless of whose turn it is
  /// </summary>
  /// <remarks>
  /// Used by actions that aren't tied to a turn, like <see cref="Resign"/>
  /// </remarks>
  /// <param name="playerId">the player id</param>
  /// <param name="player">the player's state; only valid when the result is <see cref="GameActionResult.Ok"/></param>
  /// <returns>
  /// <see cref="GameActionResult.GameOver"/> if the game hasn't started or already ended,
  /// <see cref="GameActionResult.NotInGame"/> if no such player exists,
  /// <see cref="GameActionResult.PlayerEliminated"/> if they've been eliminated, <see cref="GameActionResult.Ok"/>
  /// otherwise
  /// </returns>
  private GameActionResult CheckInGame(int playerId, out PlayerState player) {
    var found = this[playerId];
    player = found!;

    if (!Started || Over) {
      return GameActionResult.GameOver;
    }

    if (found == null) {
      return GameActionResult.NotInGame;
    }

    return found.Alive ? GameActionResult.Ok : GameActionResult.PlayerEliminated;
  }

  /// <summary>
  /// Builds the result for a request rejected before doing anything, carrying the game's unchanged state
  /// </summary>
  /// <param name="result">the rejection reason</param>
  /// <returns>the rejected result</returns>
  private EndTurnResult Rejected(GameActionResult result) => new(result, Turn, 0, Over, Winner);
}

/// <summary>
/// The outcome of a request that can end a turn: <see cref="Game.EndTurn"/> or <see cref="Game.Resign"/>
/// </summary>
/// <param name="Result">whether the request itself was accepted</param>
/// <param name="Turn">the turn number after the request</param>
/// <param name="NextPlayer">the id of the player whose turn began; 0 if the request was rejected or the game ended</param>
/// <param name="GameOver">whether the game ended as a result of this request</param>
/// <param name="Winner">the winning player's id; 0 if the game hasn't ended</param>
public readonly record struct EndTurnResult(GameActionResult Result, uint Turn, int NextPlayer, bool GameOver, int Winner);
