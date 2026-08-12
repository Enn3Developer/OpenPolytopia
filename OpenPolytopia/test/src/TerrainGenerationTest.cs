namespace OpenPolytopia;

using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Common;
using Godot;
using Shouldly;

public class TerrainGenerationTest(Node testScene) : TestClass(testScene) {
  private const uint SIZE = 16;
  private const int SEED = 42;

  private static async Task<(Grid, CityManager, Player[])> GenerateMapAsync() {
    var grid = new Grid(SIZE);
    var cityManager = new CityManager(grid);
    var tribeManager = new TribeManager();
    tribeManager.RegisterTribe(TribeType.Imperius,
      new Tribe {
        StartingTech = new StartingTech { Branch = BranchType.Organization, Id = "organization" },
        SpawnRate = new SpawnRate {
          FruitRate = 2.0f, CropRate = 1.0f, AnimalRate = 0.5f, MineralRate = 1.0f, FishRate = 1.0f
        },
        TerrainRate = new TerrainRate { ForestRate = 1.0f, MountainRate = 1.0f, WaterRate = 1.0f },
        StartingStars = 7
      });
    var players = new[] { new Player(TribeType.Imperius, 1), new Player(TribeType.Imperius, 2) };

    var terrainGeneration = new TerrainGeneration(grid, cityManager, tribeManager, players, SEED);
    await terrainGeneration.GenerateMapAsync();
    return (grid, cityManager, players);
  }

  [Test]
  public async Task TestCapitals() {
    var (grid, cityManager, players) = await GenerateMapAsync();

    var capitals = 0;
    foreach (var index in cityManager.Cities) {
      var tile = grid[index];
      tile.Kind.ShouldBe(TileKind.Village);

      var cityData = tile.GetCustomData<CityData>();
      if (!cityData.Capital) {
        continue;
      }

      capitals++;
      cityData.Level.ShouldBe(1);
      cityData.Owner.ShouldBeGreaterThan(0);
      tile.GetTileModifier<VillageTileModifier>().ShouldBe(VillageTileModifier.City);

      // capitals can't spawn on the map border
      grid.IndexToGridPosition(index, out var x, out var y);
      x.ShouldBeInRange(2u, SIZE - 3);
      y.ShouldBeInRange(2u, SIZE - 3);
    }

    capitals.ShouldBe(players.Length);
  }

  [Test]
  public async Task TestTerrain() {
    var (grid, _, _) = await GenerateMapAsync();

    // a default map should have both land and water
    var land = 0;
    var water = 0;
    for (var i = 0u; i < SIZE * SIZE; i++) {
      if (grid[i].Kind is TileKind.Water or TileKind.Ocean) {
        water++;
      }
      else {
        land++;
      }
    }

    land.ShouldBeGreaterThan(0);
    water.ShouldBeGreaterThan(0);
  }

  [Test]
  public async Task TestVillages() {
    var (grid, cityManager, players) = await GenerateMapAsync();

    var villages = 0;
    foreach (var index in cityManager.Cities) {
      if (grid[index].GetCustomData<CityData>().Capital) {
        continue;
      }

      villages++;

      // villages have no owner until captured
      grid[index].GetCustomData<CityData>().Owner.ShouldBe(0);
      grid[index].GetCustomData<CityData>().Level.ShouldBe(0);

      // villages can't spawn next to another city or village
      foreach (var other in cityManager.Cities) {
        if (other == index) {
          continue;
        }

        grid.IndexToGridPosition(index, out var x, out var y);
        grid.IndexToGridPosition(other, out var otherX, out var otherY);
        var distance = Math.Max(Math.Abs((int)x - (int)otherX), Math.Abs((int)y - (int)otherY));
        distance.ShouldBeGreaterThanOrEqualTo(2);
      }
    }

    villages.ShouldBeGreaterThan(0);
  }

  [Test]
  public async Task TestResourcesNearCities() {
    var (grid, cityManager, _) = await GenerateMapAsync();

    for (var i = 0u; i < SIZE * SIZE; i++) {
      var tile = grid[i];
      if (tile.Kind == TileKind.Village || tile.Modifier == 0) {
        continue;
      }

      // resources only spawn within 2 tiles of a city or village
      grid.IndexToGridPosition(i, out var x, out var y);
      var nearCity = false;
      foreach (var index in cityManager.Cities) {
        grid.IndexToGridPosition(index, out var cityX, out var cityY);
        var distance = Math.Max(Math.Abs((int)x - (int)cityX), Math.Abs((int)y - (int)cityY));
        if (distance <= 2) {
          nearCity = true;
          break;
        }
      }

      nearCity.ShouldBeTrue($"resource at ({x}, {y}) is more than 2 tiles away from any city");
    }
  }

  [Test]
  public async Task TestRuins() {
    var (grid, _, _) = await GenerateMapAsync();

    var ruins = 0;
    for (var i = 0u; i < SIZE * SIZE; i++) {
      if (grid[i].Ruin) {
        ruins++;
      }
    }

    // one ruin every 40 tiles
    ruins.ShouldBeGreaterThan(0);
    ruins.ShouldBeLessThanOrEqualTo((int)(SIZE * SIZE / 40));
  }

  [Test]
  public async Task TestDeterministicSeed() {
    var (first, _, _) = await GenerateMapAsync();
    var (second, _, _) = await GenerateMapAsync();

    for (var i = 0u; i < SIZE * SIZE; i++) {
      first[i].Kind.ShouldBe(second[i].Kind);
      first[i].Modifier.ShouldBe(second[i].Modifier);
      first[i].Ruin.ShouldBe(second[i].Ruin);
    }
  }
}
