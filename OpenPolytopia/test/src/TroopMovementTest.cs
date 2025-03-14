namespace OpenPolytopia;

using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using Common;
using Shouldly;

public class TroopMovementTest(Node testScene) : TestClass(testScene) {
  private static readonly JsonSerializerOptions _options = new() {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All),
    TypeInfoResolver = TroopGenerationContext.Default,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
  };

  private TroopManager _troopManager = null!;

  [Setup]
  public void Setup() {
    _troopManager = new TroopManager(10);
    var content = EmbeddedResources.TroopsData;
    var troops = JsonSerializer.Deserialize<TroopsSerializedData>(content, _options);
    troops.ShouldNotBeNull();
    _troopManager.RegisterTroops(troops);
  }

  [Test]
  public async Task TestNumbersPathAsync() {
    _troopManager.SpawnTroop(new Vector2I(0, 0), 1, 1, TroopType.Warrior);
    var counter = 0;
    var col = new TroopMovement(_troopManager, new Grid(10)).DiscoverPathAsync(new Vector2I(0, 0));
    await foreach (var _ in col) {
      counter++;
    }

    counter.ShouldBe(3);
  }

  [Test]
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

  [Test]
  public async Task TestMoveMultipleTroopsAsync() {
    _troopManager.SpawnTroop(new Vector2I(5, 0), 1, 1, TroopType.Warrior);
    _troopManager.SpawnTroop(new Vector2I(6, 0), 1, 1, TroopType.Warrior);
    var col = new TroopMovement(_troopManager, new Grid(10)).DiscoverPathAsync(new Vector2I(5, 0));
    await foreach (var pos in col) {
      pos.ShouldNotBe(new Vector2I(6, 0));
    }
  }
}
