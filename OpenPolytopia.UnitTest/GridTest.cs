namespace OpenPolytopia;

using Common;
using Godot;
using Shouldly;

public class GridTest {
  [Fact]
  public void TestPositionToIndex() {
    var grid = new Grid(10);
    grid.GridPositionToIndex(new Vector2I(2, 2)).ShouldBe(22u);
  }

  [Fact]
  public void TestIndexToPosition() {
    var grid = new Grid(10);
    grid.IndexToGridPosition(22u).ShouldBe(new Vector2I(2, 2));
  }

  [Fact]
  public void TestModifyTile() {
    var grid = new Grid(10);
    grid.ModifyTile(0, (ref Tile tile) => tile.Owner = 2);
    grid[0].Owner.ShouldBe(2);
  }
}
