namespace OpenPolytopia;

using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Common;
using Godot;
using Shouldly;

public class TerrainGenerationNodeTest(Node testScene) : TestClass(testScene) {
  [Test]
  public async Task TestGenerateMap() {
    var node = new TerrainGenerationNode {
      GenerateOnReady = false,
      GridSize = 16,
      Seed = 42,
      PlayerTribes = [(int)TribeType.Imperius, (int)TribeType.Bardur]
    };

    var emitted = false;
    node.MapGenerated += () => emitted = true;

    await node.GenerateMapAsync();

    emitted.ShouldBeTrue();
    node.Grid.ShouldNotBeNull();
    node.CityManager.ShouldNotBeNull();
    node.Players.ShouldNotBeNull();
    node.Players.Length.ShouldBe(2);

    // the tribes from the embedded tribes.json are registered automatically
    node.TribeManager[TribeType.Imperius].ShouldNotBeNull();

    var capitals = 0;
    foreach (var index in node.CityManager.Cities) {
      if (node.Grid[index].GetCustomData<CityData>().Capital) {
        capitals++;
      }
    }

    capitals.ShouldBe(2);
    node.Free();
  }

  [Test]
  public async Task TestRegenerate() {
    var node = new TerrainGenerationNode {
      GenerateOnReady = false, GridSize = 16, Seed = 42, PlayerTribes = [(int)TribeType.Imperius]
    };

    await node.GenerateMapAsync();
    var firstGrid = node.Grid;

    // every generation creates a fresh grid
    await node.GenerateMapAsync();
    node.Grid.ShouldNotBeSameAs(firstGrid);
    node.Free();
  }

  [Test]
  public async Task TestInvalidPlayers() {
    var node = new TerrainGenerationNode { GenerateOnReady = false, PlayerTribes = [] };

    await Should.ThrowAsync<System.InvalidOperationException>(node.GenerateMapAsync);
    node.Free();
  }
}
