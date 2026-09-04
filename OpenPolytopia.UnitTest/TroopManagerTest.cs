namespace OpenPolytopia;

using System.Linq;
using Godot;
using Common;
using Shouldly;

public class TroopManagerTest {
  private TroopManager _troopManager = null!;

  public TroopManagerTest() {
    _troopManager = new TroopManager(10);
    var troops = EmbeddedResources.LoadTroops();
    troops.ShouldNotBeNull();
    _troopManager.RegisterTroops(troops);
  }

  [Fact]
  public void TestSpawnTroop() {
    _troopManager.SpawnTroop(new Vector2I(0, 0), 1, 1, TroopType.Warrior);
    _troopManager[0u].IsValid().ShouldBeTrue();
    _troopManager[0u].Type.ShouldBe(TroopType.Warrior);
    _troopManager[0u].Player.ShouldBe(1u);
    _troopManager[0u].City.ShouldBe(1u);
  }

  [Fact]
  public void TestDeleteTroop() {
    var position = new Vector2I(1, 0);
    _troopManager.SpawnTroop(position, 1, 1, TroopType.Warrior);
    _troopManager[1].IsValid().ShouldBeTrue();
    _troopManager.DeleteTroop(position);
    _troopManager[1].IsValid().ShouldBeFalse();
  }

  [Fact]
  public void TestMoveTroop() {
    var initialPosition = new Vector2I(2, 0);
    var finalPosition = new Vector2I(3, 0);
    _troopManager.SpawnTroop(initialPosition, 1, 1, TroopType.Warrior);
    _troopManager[2].IsValid().ShouldBeTrue();
    _troopManager.MoveTroop(initialPosition, finalPosition);
    _troopManager[2].IsValid().ShouldBeFalse();
    _troopManager[3].IsValid().ShouldBeTrue();
  }

  [Fact]
  public void TestSetVeteran() {
    var position = new Vector2I(4, 0);
    _troopManager.SpawnTroop(position, 1, 1, TroopType.Warrior);
    _troopManager.SetVeteran(position);
    _troopManager[4].Veteran.ShouldBeTrue();
  }

  [Fact]
  public void TestModifyTroop() {
    var position = new Vector2I(5, 0);
    _troopManager.SpawnTroop(position, 1, 1, TroopType.Warrior);
    _troopManager.ModifyTroop(position, (ref TroopData troopData) => troopData.Player = 2);
    _troopManager[5].Player.ShouldBe(2u);
  }

  [Fact]
  public void TestSetRaw() {
    var raw = new TroopData { Player = 1, Type = TroopType.Rider, Hp = 5 }.Raw;
    _troopManager.SetRaw(6, raw);
    _troopManager[6].Type.ShouldBe(TroopType.Rider);
    _troopManager[6].Hp.ShouldBe(5u);
  }

  [Fact]
  public void TestTroops() {
    _troopManager.SpawnTroop(new Vector2I(7, 0), 1, 1, TroopType.Warrior);
    _troopManager.SpawnTroop(new Vector2I(8, 0), 2, 1, TroopType.Archer);
    var indexes = _troopManager.Troops().Select(entry => entry.Index).ToArray();
    indexes.ShouldBe([7u, 8u]);
  }
}
