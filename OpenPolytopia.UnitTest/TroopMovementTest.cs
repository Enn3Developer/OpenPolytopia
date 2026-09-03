namespace OpenPolytopia;

using System.Threading.Tasks;
using Godot;
using Common;
using Shouldly;

public class TroopMovementTest {
  private TroopManager _troopManager = null!;

  public TroopMovementTest() {
    _troopManager = new TroopManager(10);
    var troops = EmbeddedResources.LoadTroops();
    troops.ShouldNotBeNull();
    _troopManager.RegisterTroops(troops);
  }

  [Fact]
  public async Task TestNumbersPathAsync() {
    _troopManager.SpawnTroop(new Vector2I(0, 0), 1, 1, TroopType.Warrior);
    var counter = 0;
    var col = new TroopMovement(_troopManager, new Grid(10)).DiscoverPathAsync(new Vector2I(0, 0));
    await foreach (var _ in col) {
      counter++;
    }

    counter.ShouldBe(3);
  }

  [Fact]
  public async Task TestMoveTroopToDiscoveredPathAsync() {
    var lastPos = new Vector2I(9, 9);
    _troopManager.SpawnTroop(lastPos, 1, 1, TroopType.Warrior);
    var col = new TroopMovement(_troopManager, new Grid(10)).DiscoverPathAsync(lastPos);
    var index = _troopManager.GridPositionToIndex(lastPos);
    await foreach (var pos in col) {
      _troopManager[index].IsValid().ShouldBeTrue();
      _troopManager.MoveTroop(lastPos, pos);
      _troopManager[index].IsValid().ShouldBeFalse();
      index = _troopManager.GridPositionToIndex(pos);
      lastPos = pos;
    }
  }

  [Fact]
  public async Task TestMoveMultipleTroopsAsync() {
    _troopManager.SpawnTroop(new Vector2I(5, 0), 1, 1, TroopType.Warrior);
    _troopManager.SpawnTroop(new Vector2I(6, 0), 1, 1, TroopType.Warrior);
    var col = new TroopMovement(_troopManager, new Grid(10)).DiscoverPathAsync(new Vector2I(5, 0));
    await foreach (var pos in col) {
      pos.ShouldNotBe(new Vector2I(6, 0));
    }
  }

  [Fact]
  public void TestZoneOfControlBlocksContinuation() {
    var grid = new Grid(10);
    _troopManager.SpawnTroop(new Vector2I(0, 0), 1, 1, TroopType.Rider); // movement 2, budget 4
    _troopManager.SpawnTroop(new Vector2I(2, 0), 2, 1, TroopType.Warrior); // enemy

    var reachable = new TroopMovement(_troopManager, grid).ReachableTiles(new Vector2I(0, 0));

    // (1, 0) is adjacent to the start and still reachable, but it's also adjacent to the enemy, so the search
    // can't continue past it: without the zone of control (2, 1) would be reachable at cost 4
    reachable.ShouldContain(new Vector2I(1, 0));
    reachable.ShouldNotContain(new Vector2I(2, 1));
  }

  [Fact]
  public void TestForestStopsMovement() {
    var grid = new Grid(10);
    grid.ModifyTile(new Vector2I(1, 0), (ref Tile tile) => tile.Kind = TileKind.Forest);
    grid.ModifyTile(new Vector2I(1, 1), (ref Tile tile) => tile.Kind = TileKind.Water); // closes off the detour
    _troopManager.SpawnTroop(new Vector2I(0, 0), 1, 1, TroopType.Rider); // movement 2, budget 4

    var reachable = new TroopMovement(_troopManager, grid).ReachableTiles(new Vector2I(0, 0));

    reachable.ShouldContain(new Vector2I(1, 0)); // the forest tile itself is reachable
    reachable.ShouldNotContain(new Vector2I(2, 0)); // but the search can't continue past it
  }

  [Fact]
  public void TestMountainNeedsClimbingTech() {
    var grid = new Grid(10);
    grid.ModifyTile(new Vector2I(1, 0), (ref Tile tile) => tile.Kind = TileKind.Mountain);
    _troopManager.SpawnTroop(new Vector2I(0, 0), 1, 1, TroopType.Warrior);
    var movement = new TroopMovement(_troopManager, grid);

    movement.ReachableTiles(new Vector2I(0, 0)).ShouldNotContain(new Vector2I(1, 0));
    movement.ReachableTiles(new Vector2I(0, 0), canClimb: true).ShouldContain(new Vector2I(1, 0));
  }

  [Fact]
  public void TestRoadsOnlyDoubleRangeRoadToRoad() {
    var partialRoadGrid = new Grid(10);
    partialRoadGrid.ModifyTile(new Vector2I(1, 0), (ref Tile tile) => tile.Roads = true);
    _troopManager.SpawnTroop(new Vector2I(0, 0), 1, 1, TroopType.Warrior); // movement 1, budget 2

    var partialReachable = new TroopMovement(_troopManager, partialRoadGrid).ReachableTiles(new Vector2I(0, 0));
    // the road on (1, 0) alone doesn't help: (0, 0) has no road, so the step still costs 2
    partialReachable.ShouldContain(new Vector2I(1, 0));
    partialReachable.ShouldNotContain(new Vector2I(2, 0));

    _troopManager.DeleteTroop(new Vector2I(0, 0));
    var fullRoadGrid = new Grid(10);
    fullRoadGrid.ModifyTile(new Vector2I(0, 0), (ref Tile tile) => tile.Roads = true);
    fullRoadGrid.ModifyTile(new Vector2I(1, 0), (ref Tile tile) => tile.Roads = true);
    fullRoadGrid.ModifyTile(new Vector2I(2, 0), (ref Tile tile) => tile.Roads = true);
    _troopManager.SpawnTroop(new Vector2I(0, 0), 1, 1, TroopType.Warrior);

    var fullReachable = new TroopMovement(_troopManager, fullRoadGrid).ReachableTiles(new Vector2I(0, 0));
    // road to road: each step costs 1, so both steps fit in the budget of 2
    fullReachable.ShouldContain(new Vector2I(2, 0));
  }

  [Fact]
  public void TestCannotPassThroughFriendlyTroop() {
    var grid = new Grid(10);
    grid.ModifyTile(new Vector2I(1, 1), (ref Tile tile) => tile.Kind = TileKind.Water); // closes off the detour
    _troopManager.SpawnTroop(new Vector2I(0, 0), 1, 1, TroopType.Rider); // movement 2, budget 4
    _troopManager.SpawnTroop(new Vector2I(1, 0), 1, 1, TroopType.Warrior); // friendly, blocks the tile

    var reachable = new TroopMovement(_troopManager, grid).ReachableTiles(new Vector2I(0, 0));

    reachable.ShouldNotContain(new Vector2I(1, 0)); // occupied tiles are never entered, friend or foe
    reachable.ShouldNotContain(new Vector2I(2, 0)); // nor can the search pass through it to continue
  }

  [Fact]
  public void TestNavalTroopCannotEnterLand() {
    var grid = new Grid(10);
    grid.ModifyTile(new Vector2I(5, 5), (ref Tile tile) => tile.Kind = TileKind.Water);
    _troopManager.SpawnTroop(new Vector2I(5, 5), 1, 1, TroopType.Scout); // has the Water skill

    var reachable = new TroopMovement(_troopManager, grid).ReachableTiles(new Vector2I(5, 5));

    reachable.ShouldBeEmpty(); // every neighbor defaults to Field, which a naval troop can't enter
  }

  [Fact]
  public void TestWaterBlocksLandTroopsEvenAsPassThrough() {
    var grid = new Grid(10);
    grid.ModifyTile(new Vector2I(1, 0), (ref Tile tile) => tile.Kind = TileKind.Water);
    grid.ModifyTile(new Vector2I(1, 1), (ref Tile tile) => tile.Kind = TileKind.Water);
    _troopManager.SpawnTroop(new Vector2I(0, 0), 1, 1, TroopType.Rider); // movement 2, budget 4

    var reachable = new TroopMovement(_troopManager, grid).ReachableTiles(new Vector2I(0, 0));

    reachable.ShouldNotContain(new Vector2I(1, 0)); // can't enter water at all
    reachable.ShouldNotContain(new Vector2I(2, 0)); // and can't use it as a stepping stone either
  }
}
