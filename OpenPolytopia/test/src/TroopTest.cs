namespace OpenPolytopia;

using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

public class TroopTest(Node testScene) : TestClass(testScene) {
  [Test]
  public void TestVeteran() {
    var troop = new TroopData();
    troop.Veteran.ShouldBeFalse();
    troop.Veteran = true;
    troop.Veteran.ShouldBeTrue();
  }

  [Test]
  public void TestAttacked() {
    var troop = new TroopData();
    troop.Attacked.ShouldBeFalse();
    troop.Attacked = true;
    troop.Attacked.ShouldBeTrue();
  }

  [Test]
  public void TestMoved() {
    var troop = new TroopData();
    troop.Moved.ShouldBeFalse();
    troop.Moved = true;
    troop.Moved.ShouldBeTrue();
  }

  [Test]
  public void TestCity() {
    const uint city = 2;
    var troop = new TroopData();
    troop.City.ShouldBe(0u);
    troop.City = city;
    troop.City.ShouldBe(city);
  }

  [Test]
  public void TestHp() {
    const uint hp = 2;
    var troop = new TroopData();
    troop.Hp.ShouldBe(0u);
    troop.Hp = hp;
    troop.Hp.ShouldBe(hp);
  }

  [Test]
  public void TestType() {
    const TroopType type = TroopType.Archer;
    var troop = new TroopData();
    troop.Type.ShouldBe(TroopType.Warrior);
    troop.Type = type;
    troop.Type.ShouldBe(type);
  }

  [Test]
  public void TestPlayer() {
    const uint player = 2;
    var troop = new TroopData();
    troop.Player.ShouldBe(0u);
    troop.Player = player;
    troop.Player.ShouldBe(player);
  }

  [Test]
  public void TestResetActions() {
    var troop = new TroopData { Attacked = true, Moved = true };
    troop.ResetActions();
    troop.Attacked.ShouldBeFalse();
    troop.Moved.ShouldBeFalse();
  }

  [Test]
  public void TestIsValid() {
    var troop = new TroopData();
    troop.IsValid().ShouldBeFalse();
    troop.Player = 2;
    troop.IsValid().ShouldBeTrue();
  }

  [Test]
  public void TestDelete() {
    var troop = new TroopData { Player = 2 };
    troop.IsValid().ShouldBeTrue();
    troop.Delete();
    troop.IsValid().ShouldBeFalse();
  }
}
