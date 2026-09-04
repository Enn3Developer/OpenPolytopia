namespace OpenPolytopia;

using System;
using System.Collections.Generic;
using Common;
using Common.Gameplay;
using Godot;
using Shouldly;

/// <summary>
/// Builds a small, deterministic <see cref="Game"/> for tests, without going through <see cref="TerrainGeneration"/>
/// </summary>
/// <remarks>
/// Shared by every gameplay test file (<c>GameTest</c>, and the troop/economy tests added by later PRs) so they all
/// start from the same known board: a flat <see cref="SIZE"/>x<see cref="SIZE"/> grid of fields with one capital per
/// player, each claiming its own 3x3 territory like <see cref="TerrainGeneration"/> does
/// </remarks>
public static class GameTestFixture {
  /// <summary>The width/height of the grid built by this fixture</summary>
  public const uint SIZE = 8;

  // spaced so their 3x3 territories never overlap on an 8x8 grid, however many of them are used
  private static readonly Vector2I[] _capitalPositions = [new(1, 1), new(6, 1), new(1, 6), new(6, 6)];

  /// <summary>
  /// Shortcut to build a grid position
  /// </summary>
  /// <param name="x">the x coordinate</param>
  /// <param name="y">the y coordinate</param>
  public static Vector2I P(int x, int y) => new(x, y);

  /// <summary>
  /// Every piece a <see cref="Game"/> is built from, exposed so a test can add troops or tweak tiles before
  /// constructing/starting the game
  /// </summary>
  /// <param name="Grid">the grid: empty fields except for the registered capitals</param>
  /// <param name="Cities">the city manager, with one capital city registered per player</param>
  /// <param name="Troops">the (empty) troop manager, with the real troop definitions registered</param>
  /// <param name="Tribes">the tribe manager, with the real tribes registered</param>
  /// <param name="Buildings">the building manager, with the real building definitions registered</param>
  /// <param name="TechTree">the real tech tree definition</param>
  /// <param name="Players">the players, in turn order, ids 1..N</param>
  /// <param name="CapitalIds">the city id of each player's capital, in the same order as <c>Players</c></param>
  public readonly record struct Pieces(Grid Grid, CityManager Cities, TroopManager Troops, TribeManager Tribes,
    BuildingManager Buildings, TechTreeDefinition TechTree, IReadOnlyList<Player> Players,
    IReadOnlyList<uint> CapitalIds);

  /// <summary>
  /// Builds every piece needed for a game, with one capital registered per player, but nothing else placed
  /// </summary>
  /// <param name="playerCount">how many players; 2 to 4</param>
  /// <returns>the pieces, ready to build a <see cref="Game"/> from, or to tweak further first</returns>
  /// <exception cref="ArgumentOutOfRangeException">if <paramref name="playerCount"/> isn't between 2 and 4</exception>
  public static Pieces BuildPieces(int playerCount = 2) {
    if (playerCount < 2 || playerCount > _capitalPositions.Length) {
      throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount,
        $"the fixture only has {_capitalPositions.Length} predefined, non-overlapping capital positions");
    }

    var grid = new Grid(SIZE);
    var cities = new CityManager(grid);
    var troops = new TroopManager(SIZE);
    var tribes = new TribeManager();
    var buildings = new BuildingManager();

    troops.RegisterTroops(EmbeddedResources.LoadTroops().ShouldNotBeNull());
    tribes.RegisterTribes(EmbeddedResources.LoadTribes().ShouldNotBeNull());
    buildings.RegisterBuildings(EmbeddedResources.LoadBuildings().ShouldNotBeNull());
    var techTree = TechTreeDefinition.FromSerializedData(EmbeddedResources.LoadTechTree().ShouldNotBeNull());

    var players = new List<Player>(playerCount);
    var capitalIds = new List<uint>(playerCount);
    for (var i = 0; i < playerCount; i++) {
      var playerId = i + 1;
      players.Add(new Player(TribeType.Imperius, playerId));
      capitalIds.Add(RegisterCapital(grid, cities, _capitalPositions[i], playerId));
    }

    return new Pieces(grid, cities, troops, tribes, buildings, techTree, players, capitalIds);
  }

  /// <summary>
  /// Builds a game with one capital registered per player, but doesn't start it
  /// </summary>
  /// <param name="playerCount">how many players; 2 to 4</param>
  /// <param name="settings">the game settings; defaults to no turn limit</param>
  /// <returns>the game, not started</returns>
  public static Game NewGame(int playerCount = 2, GameSettings? settings = null) {
    var pieces = BuildPieces(playerCount);
    return new Game(pieces.Grid, pieces.Cities, pieces.Troops, pieces.Tribes, pieces.Buildings, pieces.TechTree,
      pieces.Players, settings);
  }

  /// <summary>
  /// Builds and starts a game
  /// </summary>
  /// <param name="playerCount">how many players; 2 to 4</param>
  /// <param name="settings">the game settings; defaults to no turn limit</param>
  /// <returns>the started game</returns>
  public static Game NewStartedGame(int playerCount = 2, GameSettings? settings = null) {
    var game = NewGame(playerCount, settings);
    game.Start();
    return game;
  }

  /// <summary>
  /// Registers a capital exactly like <see cref="TerrainGeneration"/> does: a level 1 city tile owned by the
  /// player, with the surrounding 3x3 territory claimed
  /// </summary>
  /// <param name="grid">the grid to register the capital on</param>
  /// <param name="cities">the city manager to register the capital in</param>
  /// <param name="position">the position of the capital</param>
  /// <param name="playerId">the owning player</param>
  /// <returns>the id of the registered city</returns>
  private static uint RegisterCapital(Grid grid, CityManager cities, Vector2I position, int playerId) {
    var cityId = cities.RegisterCity(position);

    grid.ModifyTile(position, (ref Tile tile) => {
      tile.Kind = TileKind.Village;
      tile.Modifier = (int)VillageTileModifier.City;
      tile.Owner = playerId;
      tile.Biome = TribeType.Imperius;
    });

    cities.ModifyCity(cityId, (ref CityData city) => {
      city.Capital = true;
      city.Owner = playerId;
      city.Level = 1;
    });

    for (var dy = -1; dy <= 1; dy++) {
      for (var dx = -1; dx <= 1; dx++) {
        var neighbor = new Vector2I(position.X + dx, position.Y + dy);
        if (neighbor.X < 0 || neighbor.Y < 0 || neighbor.X >= (int)grid.Size || neighbor.Y >= (int)grid.Size) {
          continue;
        }

        // never steal a tile already claimed by another capital
        grid.ModifyTile(neighbor, (ref Tile tile) => {
          if (tile.Owner != 0 || tile.Kind == TileKind.Village) {
            return;
          }

          tile.Owner = playerId;
          tile.City = (int)cityId;
        });
      }
    }

    return cityId;
  }
}
