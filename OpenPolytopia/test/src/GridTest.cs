namespace OpenPolytopia;

using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

public class GridTest(Node testScene) : TestClass(testScene) {
  [Test]
  public void TestPositionToIndex() {
    var grid = new Grid(10);
    grid.GridPositionToIndex(new Vector2I(2, 2)).ShouldBe(22u);
  }

  [Test]
  public void TestModifyTile() {
    var grid = new Grid(10);
    grid.ModifyTile(0, (ref Tile tile) => tile.Owner = 2);
    grid[0].Owner.ShouldBe(2);
  }
}
