namespace OpenPolytopia.Common;

using System.Runtime.CompilerServices;

/// <summary>
/// Terrain generation system
/// </summary>
/// <param name="grid">the grid to use</param>
/// <param name="cityManager">the city manager to use</param>
/// <param name="tribeManager">the tribe manager to use</param>
/// <param name="players">all the players in the game</param>
/// <param name="seed">the optional random seed</param>
public class TerrainGeneration(
  Grid grid,
  CityManager cityManager,
  TribeManager tribeManager,
  Player[] players,
  int? seed = null) {
  // create the random number generator from a seed if set
  private readonly Random _rng = seed == null ? new Random() : new Random(seed.Value);

  /// <summary>
  /// Generates the map ready to use in-game
  /// </summary>
  /// <example>
  /// <code>
  /// var terrainGeneration = new TerrainGeneration(grid, cityManager, tribeManager);
  /// await terrainGeneration.GenerateMapAsync();
  /// </code>
  /// </example>
  public async Task GenerateMapAsync() {
    await GenerateInitialCitiesAsync();
    await GenerateTerrainAsync();
    await GenerateCitiesAsync();
    await GenerateResourcesAsync();
  }

  private async Task GenerateInitialCitiesAsync() {
    // for each player
    foreach (var player in players) {
      // yield to the task executor
      await Task.Yield();

      // compute the index position of a random tile in the grid
      var index = (uint)_rng.Next(0, (int)(grid.Size * grid.Size));

      // check if the computed index is valid
      var valid = false;
      while (!valid) {
        if (IsCityValid(index)) {
          valid = true;
        }
        else {
          index = (uint)_rng.Next(0, (int)(grid.Size * grid.Size));
        }
      }

      // register the city
      var cityId = cityManager.RegisterCity(index);

      // set all data for the tile in the grid
      grid.ModifyTile(index, (ref Tile tile) => {
        tile.Owner = player.Id;
        tile.Kind = TileKind.Village;
        tile.Modifier = (int)VillageTileModifier.City;
      });

      // set all data for the city
      cityManager.ModifyCity(cityId, (ref CityData city) => {
        city.Capital = true;
        city.Owner = player.Id;
        city.Level = 1;
      });
    }
  }

  private async Task GenerateTerrainAsync() {
  }

  private async Task GenerateCitiesAsync() {
  }

  private async Task GenerateResourcesAsync() {
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool IsCityValid(uint index) {
    grid.IndexToGridPosition(index, out var x, out var y);
    return grid[index].Owner == 0 && x > 0 && y > 0 && x < grid.Size - 1 && y < grid.Size - 1;
  }
}
