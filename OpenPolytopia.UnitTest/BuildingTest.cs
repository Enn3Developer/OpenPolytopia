namespace OpenPolytopia;

using System;
using Common;
using Shouldly;

public class BuildingTest {
  private static Building MakeBuilding() =>
    new() { Kind = TileKind.Field, TileBuilding = 0, Cost = 1, Population = 0 };

  [Fact]
  public void TestLoadEmbeddedRegistersEveryType() {
    var data = EmbeddedResources.LoadBuildings();
    data.ShouldNotBeNull();
    var manager = new BuildingManager();
    manager.RegisterBuildings(data);

    foreach (var type in Enum.GetValues<BuildingType>()) {
      manager[type].ShouldNotBeNull($"{type} isn't registered");
    }
  }

  [Fact]
  public void TestDuplicateRegistrationThrows() {
    var manager = new BuildingManager();
    manager.RegisterBuilding(BuildingType.Road, MakeBuilding());
    Should.Throw<ArgumentException>(() => manager.RegisterBuilding(BuildingType.Road, MakeBuilding()));
  }

  [Fact]
  public void TestUndefinedTypeThrows() {
    var manager = new BuildingManager();
    Should.Throw<ArgumentException>(() => manager.RegisterBuilding((BuildingType)42, MakeBuilding()));
  }

  [Theory]
  [InlineData(ResourceType.Fruit, TileKind.Field, (int)FieldTileModifier.Fruit)]
  [InlineData(ResourceType.Crop, TileKind.Field, (int)FieldTileModifier.Crop)]
  [InlineData(ResourceType.Animal, TileKind.Forest, (int)ForestTileModifier.Animal)]
  [InlineData(ResourceType.Fish, TileKind.Water, (int)WaterTileModifier.Fish)]
  [InlineData(ResourceType.Mineral, TileKind.Mountain, (int)MountainTileModifier.Ore)]
  public void TestResourceMapping(ResourceType resource, TileKind expectedKind, int expectedModifier) {
    BuildingManager.TryGetResourceTile(resource, out var kind, out var modifier).ShouldBeTrue();
    kind.ShouldBe(expectedKind);
    modifier.ShouldBe(expectedModifier);
  }

  [Fact]
  public void TestNoneResourceMapping() =>
    BuildingManager.TryGetResourceTile(ResourceType.None, out _, out _).ShouldBeFalse();
}
