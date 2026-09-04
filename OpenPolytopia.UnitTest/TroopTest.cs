namespace OpenPolytopia;

using System.Linq;
using Common;
using Godot;
using Shouldly;

public class TroopTest {
  [Fact]
  public void TestVeteran() {
    var troop = new TroopData();
    troop.Veteran.ShouldBeFalse();
    troop.Veteran = true;
    troop.Veteran.ShouldBeTrue();
  }

  [Fact]
  public void TestAttacked() {
    var troop = new TroopData();
    troop.Attacked.ShouldBeFalse();
    troop.Attacked = true;
    troop.Attacked.ShouldBeTrue();
  }

  [Fact]
  public void TestMoved() {
    var troop = new TroopData();
    troop.Moved.ShouldBeFalse();
    troop.Moved = true;
    troop.Moved.ShouldBeTrue();
  }

  [Fact]
  public void TestCity() {
    const uint city = 2;
    var troop = new TroopData();
    troop.City.ShouldBe(0u);
    troop.City = city;
    troop.City.ShouldBe(city);
  }

  [Fact]
  public void TestHp() {
    const uint hp = 2;
    var troop = new TroopData();
    troop.Hp.ShouldBe(0u);
    troop.Hp = hp;
    troop.Hp.ShouldBe(hp);
  }

  [Fact]
  public void TestType() {
    const TroopType type = TroopType.Archer;
    var troop = new TroopData();
    troop.Type.ShouldBe(TroopType.Warrior);
    troop.Type = type;
    troop.Type.ShouldBe(type);
  }

  [Fact]
  public void TestPlayer() {
    const uint player = 2;
    var troop = new TroopData();
    troop.Player.ShouldBe(0u);
    troop.Player = player;
    troop.Player.ShouldBe(player);
  }

  [Fact]
  public void TestResetActions() {
    var troop = new TroopData { Attacked = true, Moved = true };
    troop.ResetActions();
    troop.Attacked.ShouldBeFalse();
    troop.Moved.ShouldBeFalse();
  }

  [Fact]
  public void TestIsValid() {
    var troop = new TroopData();
    troop.IsValid().ShouldBeFalse();
    troop.Player = 2;
    troop.IsValid().ShouldBeTrue();
  }

  [Fact]
  public void TestDelete() {
    var troop = new TroopData { Player = 2 };
    troop.IsValid().ShouldBeTrue();
    troop.Delete();
    troop.IsValid().ShouldBeFalse();
  }

  [Fact]
  public void TestPlayerSixteen() {
    // the lobby allows up to 16 players (ids 1-16), 4 bits could only hold up to 15
    var troop = new TroopData { Player = 16 };
    troop.Player.ShouldBe(16u);
  }

  [Fact]
  public void TestRaw() {
    var troop = new TroopData {
      Player = 16, Type = TroopType.Archer, Hp = 7, City = 3, Moved = true, Veteran = true
    };
    var copy = new TroopData { Raw = troop.Raw };
    copy.Player.ShouldBe(16u);
    copy.Type.ShouldBe(TroopType.Archer);
    copy.Hp.ShouldBe(7u);
    copy.City.ShouldBe(3u);
    copy.Moved.ShouldBeTrue();
    copy.Veteran.ShouldBeTrue();
  }

  [Fact]
  public void TestTroopsResourceCostAndTech() {
    var data = EmbeddedResources.LoadTroops();
    data.ShouldNotBeNull();

    var warrior = data.Troops.Single(entry => entry.Type == TroopType.Warrior).Troop;
    warrior.Cost.ShouldBe(2u);
    warrior.RequiredTech.ShouldBeNull();

    var knight = data.Troops.Single(entry => entry.Type == TroopType.Knight).Troop;
    knight.Cost.ShouldBe(8u);
    knight.RequiredTech.ShouldBe("chivalry");
  }
}
