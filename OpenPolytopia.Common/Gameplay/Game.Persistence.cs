namespace OpenPolytopia.Common.Gameplay;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Capture and restore of a whole match, for persisting a game between two runs of the server
/// </summary>
/// <remarks>
/// A snapshot only carries what a game mutates; everything else it's built from is content, so
/// <see cref="Game.Restore"/> asks for it again and a restored game shares nothing with the one it came from
/// </remarks>
public partial class Game {
  /// <summary>
  /// Rebuilds a game from its parts, without running any of the setup the public constructor does
  /// </summary>
  /// <remarks>
  /// The public constructor builds every <see cref="PlayerState"/> from the tribe, which is exactly what a restore
  /// can't do: a restored player has the stars, the score and the researched nodes of the snapshot, not the ones a
  /// tribe starts with
  /// </remarks>
  /// <param name="grid">the grid, already filled</param>
  /// <param name="cityManager">the city manager, with every city already registered</param>
  /// <param name="troopManager">the troop manager, already filled</param>
  /// <param name="buildingManager">the registered building definitions</param>
  /// <param name="settings">the settings of the game</param>
  /// <param name="players">the players, in turn order, already restored</param>
  private Game(Grid grid, CityManager cityManager, TroopManager troopManager, BuildingManager buildingManager,
    GameSettings settings, List<PlayerState> players) {
    Grid = grid;
    Cities = cityManager;
    Troops = troopManager;
    Buildings = buildingManager;
    Settings = settings;
    Players = players;
    _playersById = players.ToDictionary(player => player.Id);
    _turnIndexById = players.Select((player, index) => (player.Id, index))
      .ToDictionary(entry => entry.Id, entry => entry.index);
  }

  /// <summary>
  /// Captures everything this game mutates into a snapshot
  /// </summary>
  /// <remarks>
  /// The snapshot owns its arrays, so playing on after taking one never changes what was captured
  /// </remarks>
  /// <returns>the snapshot, restorable with <see cref="Restore"/></returns>
  public GameSnapshot ToSnapshot() {
    var cells = Grid.Size * Grid.Size;
    var tiles = new ulong[cells];
    var troops = new uint[cells];

    for (var index = 0u; index < cells; index++) {
      tiles[index] = Grid[index].Raw;
      troops[index] = Troops[index].Raw;
    }

    return new GameSnapshot {
      Version = GameSnapshot.CURRENT_VERSION,
      GridSize = Grid.Size,
      Tiles = tiles,
      Troops = troops,
      CityIndexes = [.. Cities.Cities],
      Players = [
        .. Players.Select(player => new PlayerSnapshot {
          Id = player.Id,
          Tribe = player.Tribe,
          Stars = player.Stars,
          Score = player.Score.ScoreValue,
          Alive = player.Alive,
          ResearchedTechs = [.. player.TechTree.ResearchedIds()]
        })
      ],
      MaxTurns = Settings.MaxTurns,
      Turn = Turn,
      CurrentPlayer = CurrentPlayer,
      Started = Started,
      Over = Over,
      Winner = Winner
    };
  }

  /// <summary>
  /// Rebuilds the game a snapshot was taken from
  /// </summary>
  /// <remarks>
  /// The content arguments have to be the same the original game was built from: a snapshot stores the researched
  /// node ids, not the tech tree, and the raw tiles/troops, not the definitions they refer to, so restoring against
  /// different content gives a game that isn't the one that was saved
  /// <br/>
  /// <paramref name="troopManager"/> is taken instead of built because only the caller has the troop definitions to
  /// register in it; it must be empty and sized like the grid of the snapshot. The grid and the city manager are
  /// built here because both are fully described by the snapshot
  /// </remarks>
  /// <param name="snapshot">the snapshot to restore</param>
  /// <param name="troopManager">an empty troop manager, sized <see cref="GameSnapshot.GridSize"/>, with the troop definitions registered</param>
  /// <param name="tribeManager">the registered tribes</param>
  /// <param name="buildingManager">the registered building definitions</param>
  /// <param name="techTreeDefinition">the shape of the tech tree the original game was built from</param>
  /// <returns>the restored game, ready to be played on</returns>
  /// <exception cref="ArgumentNullException">if any argument, or any member of the snapshot, is null</exception>
  /// <exception cref="ArgumentException">
  /// if the snapshot wasn't written by <see cref="GameSnapshot.CURRENT_VERSION"/>, if its arrays don't have one entry per
  /// grid cell, if <paramref name="troopManager"/> isn't sized like the grid, if it doesn't have 2 to 16 players, if
  /// a player id is out of range or used twice, if a tribe isn't registered, if a researched node isn't in the tech
  /// tree of its player, if a city index is outside the grid or if there are more than 255 cities
  /// </exception>
  public static Game Restore(GameSnapshot snapshot, TroopManager troopManager, TribeManager tribeManager,
    BuildingManager buildingManager, TechTreeDefinition techTreeDefinition) {
    ArgumentNullException.ThrowIfNull(snapshot);
    ArgumentNullException.ThrowIfNull(troopManager);
    ArgumentNullException.ThrowIfNull(tribeManager);
    ArgumentNullException.ThrowIfNull(buildingManager);
    ArgumentNullException.ThrowIfNull(techTreeDefinition);
    ArgumentNullException.ThrowIfNull(snapshot.Tiles);
    ArgumentNullException.ThrowIfNull(snapshot.Troops);
    ArgumentNullException.ThrowIfNull(snapshot.CityIndexes);
    ArgumentNullException.ThrowIfNull(snapshot.Players);

    if (snapshot.Version != GameSnapshot.CURRENT_VERSION) {
      throw new ArgumentException(
        $"snapshot version {snapshot.Version} can't be restored; this build writes version {GameSnapshot.CURRENT_VERSION}",
        nameof(snapshot));
    }

    if (snapshot.GridSize == 0) {
      throw new ArgumentException("a snapshot of a game has a grid", nameof(snapshot));
    }

    var cells = snapshot.GridSize * snapshot.GridSize;
    if (snapshot.Tiles.Length != cells) {
      throw new ArgumentException(
        $"a {snapshot.GridSize}x{snapshot.GridSize} grid has {cells} tiles, the snapshot has {snapshot.Tiles.Length}",
        nameof(snapshot));
    }

    if (snapshot.Troops.Length != cells) {
      throw new ArgumentException(
        $"a {snapshot.GridSize}x{snapshot.GridSize} grid has {cells} cells, the snapshot has {snapshot.Troops.Length} troop slots",
        nameof(snapshot));
    }

    if (troopManager.Size != snapshot.GridSize) {
      throw new ArgumentException(
        $"the troop manager is sized {troopManager.Size}, the snapshot needs {snapshot.GridSize}", nameof(troopManager));
    }

    if (snapshot.Players.Count is < 2 or > 16) {
      throw new ArgumentException($"a game needs 2 to 16 players, the snapshot has {snapshot.Players.Count}",
        nameof(snapshot));
    }

    var grid = new Grid(snapshot.GridSize);
    var cityManager = new CityManager(grid);

    // the cities go in first: registering one writes into its tile, so the raw tiles have to be written after it
    foreach (var index in snapshot.CityIndexes) {
      if (index >= cells) {
        throw new ArgumentException($"city index {index} is outside a {snapshot.GridSize}x{snapshot.GridSize} grid",
          nameof(snapshot));
      }

      cityManager.RegisterCity(index);
    }

    for (var index = 0u; index < cells; index++) {
      grid[index] = new Tile { Raw = snapshot.Tiles[index] };
      troopManager.SetRaw(index, snapshot.Troops[index]);
    }

    var players = new List<PlayerState>(snapshot.Players.Count);
    var seenIds = new HashSet<int>(snapshot.Players.Count);

    foreach (var playerSnapshot in snapshot.Players) {
      ArgumentNullException.ThrowIfNull(playerSnapshot, nameof(snapshot));
      ArgumentNullException.ThrowIfNull(playerSnapshot.ResearchedTechs, nameof(snapshot));

      if (playerSnapshot.Id is < 1 or > 16) {
        throw new ArgumentException($"player id {playerSnapshot.Id} is out of range; ids must be between 1 and 16",
          nameof(snapshot));
      }

      if (!seenIds.Add(playerSnapshot.Id)) {
        throw new ArgumentException($"player id {playerSnapshot.Id} is used by more than one player", nameof(snapshot));
      }

      var tribe = tribeManager[playerSnapshot.Tribe] ??
        throw new ArgumentException($"tribe {playerSnapshot.Tribe} of player {playerSnapshot.Id} isn't registered",
          nameof(snapshot));

      players.Add(RestorePlayer(playerSnapshot, tribe, techTreeDefinition));
    }

    var game = new Game(grid, cityManager, troopManager, buildingManager,
      new GameSettings { MaxTurns = snapshot.MaxTurns }, players) {
      Turn = snapshot.Turn,
      CurrentPlayer = snapshot.CurrentPlayer,
      Started = snapshot.Started,
      Over = snapshot.Over,
      Winner = snapshot.Winner
    };

    // a started, unfinished game hands the turn to somebody, and turn order is looked up by that id
    if (game is { Started: true, Over: false } && !game._turnIndexById.ContainsKey(game.CurrentPlayer)) {
      throw new ArgumentException($"player {snapshot.CurrentPlayer} holds the turn but isn't in the game",
        nameof(snapshot));
    }

    return game;
  }

  /// <summary>
  /// Rebuilds the state of a single player
  /// </summary>
  /// <remarks>
  /// The tech tree is built empty from the definition of the tribe, with the overrides applied exactly like
  /// <see cref="TechTreeDefinition.CreateTechTree"/> does, and then only what the snapshot says was researched is
  /// marked: the starting node of the tribe isn't researched for free, so a snapshot restores the researched set it
  /// captured and nothing else
  /// </remarks>
  /// <param name="snapshot">the snapshot of the player</param>
  /// <param name="tribe">the tribe of the player</param>
  /// <param name="techTreeDefinition">the shape of the tech tree, before the overrides of the tribe</param>
  /// <returns>the restored state</returns>
  /// <exception cref="ArgumentException">if a researched node isn't in the tech tree of this player</exception>
  private static PlayerState RestorePlayer(PlayerSnapshot snapshot, Tribe tribe,
    TechTreeDefinition techTreeDefinition) {
    var definition = tribe.TechOverrides is { Count: > 0 } overrides
      ? techTreeDefinition.Override(overrides)
      : techTreeDefinition;
    var techTree = new TechTree(definition);

    foreach (var techId in snapshot.ResearchedTechs) {
      if (!techTree.Research(techId)) {
        throw new ArgumentException($"node {techId} of player {snapshot.Id} isn't in the tech tree", nameof(snapshot));
      }
    }

    var player = new PlayerState(snapshot.Id, snapshot.Tribe, snapshot.Stars, techTree) { Alive = snapshot.Alive };
    player.Score.Restore(snapshot.Score);
    return player;
  }
}
