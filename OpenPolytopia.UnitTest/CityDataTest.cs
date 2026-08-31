namespace OpenPolytopia;

using Common;
using Godot;
using Shouldly;

public class CityDataTest {
  [Fact]
  public void TestOwner() {
    const int owner = 2;
    var cityData = new CityData();
    cityData.Owner.ShouldBe(0);
    cityData.Owner = owner;
    cityData.Owner.ShouldBe(owner);
  }

  [Fact]
  public void TestLevel() {
    const int level = 2;
    var cityData = new CityData();
    cityData.Level.ShouldBe(0);
    cityData.Level = level;
    cityData.Level.ShouldBe(level);
  }

  [Fact]
  public void TestMaxPopulation() {
    const int level = 2;
    var cityData = new CityData();
    cityData.MaxPopulation.ShouldBe(1);
    cityData.Level = level;
    cityData.MaxPopulation.ShouldBe(level + 1);
  }

  [Fact]
  public void TestPopulation() {
    const int population = 2;
    var cityData = new CityData();
    cityData.Population.ShouldBe(0);
    cityData.Population = population;
    cityData.Population.ShouldBe(population);
  }

  [Fact]
  public void TestTroops() {
    const int troops = 2;
    var cityData = new CityData();
    cityData.Troops.ShouldBe(0);
    cityData.Troops = troops;
    cityData.Troops.ShouldBe(troops);
  }

  [Fact]
  public void TestParks() {
    const int parks = 2;
    var cityData = new CityData();
    cityData.Parks.ShouldBe(0);
    cityData.Parks = parks;
    cityData.Parks.ShouldBe(parks);
  }

  [Fact]
  public void TestWall() {
    const bool wall = true;
    var cityData = new CityData();
    cityData.Wall.ShouldBe(false);
    cityData.Wall = wall;
    cityData.Wall.ShouldBe(wall);
  }

  [Fact]
  public void TestForge() {
    const bool forge = true;
    var cityData = new CityData();
    cityData.Forge.ShouldBe(false);
    cityData.Forge = forge;
    cityData.Forge.ShouldBe(forge);
  }

  [Fact]
  public void TestCapital() {
    const bool capital = true;
    var cityData = new CityData();
    cityData.Capital.ShouldBe(false);
    cityData.Capital = capital;
    cityData.Capital.ShouldBe(capital);
  }

  [Fact]
  public void TestConnected() {
    const bool connected = true;
    var cityData = new CityData();
    cityData.Connected.ShouldBe(false);
    cityData.Connected = connected;
    cityData.Connected.ShouldBe(connected);
  }

  [Fact]
  public void TestStars() {
    const int level = 2;
    var cityData = new CityData();
    cityData.Stars.ShouldBe(0);
    cityData.Level = level;
    cityData.Stars.ShouldBe(2);
    cityData.Capital = true;
    cityData.Stars.ShouldBe(3);
    cityData.Forge = true;
    cityData.Stars.ShouldBe(4);
    cityData.Parks = 3;
    cityData.Stars.ShouldBe(7);
  }

  [Fact]
  public void TestLevelUp() {
    var cityData = new CityData();
    var result = cityData.LevelUp();
    result.ShouldBe(false);
    cityData.Level = 1;
    cityData.Population = 2;
    result = cityData.LevelUp();
    result.ShouldBe(true);
    cityData.Level.ShouldBe(2);
    cityData.Population.ShouldBe(0);
  }
}
