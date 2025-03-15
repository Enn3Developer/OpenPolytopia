namespace OpenPolytopia.test.src;

using System.Text.Json;
using System.Text.Unicode;
using Chickensoft.GoDotTest;
using Godot;
using Common;
using Shouldly;

public class TroopManagerTest(Node testScene) : TestClass(testScene) {
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
  public void TestSpawnTroop() {
    _troopManager.SpawnTroop(new Vector2I(0, 0), 1, 1, TroopType.Warrior);
    _troopManager[0u].IsValid().ShouldBeTrue();
    _troopManager[0u].Type.ShouldBe(TroopType.Warrior);
    _troopManager[0u].Player.ShouldBe(1u);
    _troopManager[0u].City.ShouldBe(1u);
  }

  [Test]
  public void TestDeleteTroop() {
    var position = new Vector2I(1, 0);
    _troopManager.SpawnTroop(position, 1, 1, TroopType.Warrior);
    _troopManager[1].IsValid().ShouldBeTrue();
    _troopManager.DeleteTroop(position);
    _troopManager[1].IsValid().ShouldBeFalse();
  }

  [Test]
  public void TestMoveTroop() {
    var initialPosition = new Vector2I(2, 0);
    var finalPosition = new Vector2I(3, 0);
    _troopManager.SpawnTroop(initialPosition, 1, 1, TroopType.Warrior);
    _troopManager[2].IsValid().ShouldBeTrue();
    _troopManager.MoveTroop(initialPosition, finalPosition);
    _troopManager[2].IsValid().ShouldBeFalse();
    _troopManager[3].IsValid().ShouldBeTrue();
  }

  [Test]
  public void TestSetVeteran() {
    var position = new Vector2I(4, 0);
    _troopManager.SpawnTroop(position, 1, 1, TroopType.Warrior);
    _troopManager.SetVeteran(position);
    _troopManager[4].Veteran.ShouldBeTrue();
  }

  [Test]
  public void TestModifyTroop() {
    var position = new Vector2I(5, 0);
    _troopManager.SpawnTroop(position, 1, 1, TroopType.Warrior);
    _troopManager.ModifyTroop(position, (ref TroopData troopData) => troopData.Player = 2);
    _troopManager[5].Player.ShouldBe(2u);
  }
}
