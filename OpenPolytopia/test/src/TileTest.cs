namespace OpenPolytopia;

using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

public class TileTest(Node testScene) : TestClass(testScene) {
  [Test]
  public void TestTileCreation() {
    const TileKind kind = TileKind.Mountain;
    var tile = new Tile(kind);
    tile.Kind.ShouldBe(kind);
  }

  [Test]
  public void TestRoad() {
    var tile = new Tile(TileKind.Field);
    tile.Roads.ShouldBe(false);
    tile.Roads = true;
    tile.Roads.ShouldBe(true);
  }

  [Test]
  public void TestRuin() {
    var tile = new Tile(TileKind.Field);
    tile.Ruin.ShouldBe(false);
    tile.Ruin = true;
    tile.Ruin.ShouldBe(true);
  }

  [Test]
  public void TestModifier() {
    const FieldTileModifier modifier = FieldTileModifier.Fruit;
    var tile = new Tile(TileKind.Field);
    tile.Modifier.ShouldBe(0);
    tile.SetTileModifier(modifier);
    tile.GetTileModifier<FieldTileModifier>().ShouldBe(modifier);
  }

  [Test]
  public void TestBuilding() {
    const FieldTileBuilding building = FieldTileBuilding.Market;
    var tile = new Tile(TileKind.Field);
    tile.Building.ShouldBe(0);
    tile.SetTileBuilding(building);
    tile.GetTileBuilding<FieldTileBuilding>().ShouldBe(building);
  }

  [Test]
  public void TestOwner() {
    const int owner = 2;
    var tile = new Tile(TileKind.Field);
    tile.Owner.ShouldBe(0);
    tile.Owner = owner;
    tile.Owner.ShouldBe(owner);
  }

  [Test]
  public void TestBiome() {
    const Tribe biome = Tribe.ElyrionDark;
    var tile = new Tile(TileKind.Field);
    tile.Biome.ShouldBe(Tribe.Imperius);
    tile.Biome = biome;
    tile.Biome.ShouldBe(biome);
  }

  [Test]
  public void TestCity() {
    const int city = 20;
    var tile = new Tile(TileKind.Field);
    tile.City.ShouldBe(0);
    tile.City = city;
    tile.City.ShouldBe(city);
  }

  [Test]
  public void TestWonder() {
    const Wonder wonder = Wonder.EmperorsTomb;
    var tile = new Tile(TileKind.Field);
    tile.Wonder.ShouldBe(Wonder.None);
    tile.Wonder = wonder;
    tile.Wonder.ShouldBe(wonder);
  }
}
