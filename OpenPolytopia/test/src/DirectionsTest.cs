namespace OpenPolytopia;

using System.Linq;
using Chickensoft.GoDotTest;
using Common;
using Godot;
using Shouldly;

public class DirectionsTest(Node testScene) : TestClass(testScene) {
  [Test]
  public void TestCardinalVectors() {
    Direction.Up.ToVector2I().ShouldBe(new Vector2I(0, -1));
    Direction.Down.ToVector2I().ShouldBe(new Vector2I(0, 1));
    Direction.Left.ToVector2I().ShouldBe(new Vector2I(-1, 0));
    Direction.Right.ToVector2I().ShouldBe(new Vector2I(1, 0));
  }

  [Test]
  public void TestDiagonalVectors() {
    Direction.UpLeft.ToVector2I().ShouldBe(new Vector2I(-1, -1));
    Direction.UpRight.ToVector2I().ShouldBe(new Vector2I(1, -1));
    Direction.DownLeft.ToVector2I().ShouldBe(new Vector2I(-1, 1));
    Direction.DownRight.ToVector2I().ShouldBe(new Vector2I(1, 1));
  }

  [Test]
  public void TestDiagonalsAreCardinalSums() {
    Direction.UpLeft.ToVector2I().ShouldBe(Direction.Up.ToVector2I() + Direction.Left.ToVector2I());
    Direction.UpRight.ToVector2I().ShouldBe(Direction.Up.ToVector2I() + Direction.Right.ToVector2I());
    Direction.DownLeft.ToVector2I().ShouldBe(Direction.Down.ToVector2I() + Direction.Left.ToVector2I());
    Direction.DownRight.ToVector2I().ShouldBe(Direction.Down.ToVector2I() + Direction.Right.ToVector2I());
  }

  [Test]
  public void TestAllDirectionsAreDistinct() {
    // a duplicated vector means a whole direction is unreachable when pathfinding
    WrapperDirection.Directions.Length.ShouldBe(8);
    WrapperDirection.Directions.Distinct().Count().ShouldBe(8);
  }

  [Test]
  public void TestOppositeDirectionsCancelOut() {
    (Direction.Up.ToVector2I() + Direction.Down.ToVector2I()).ShouldBe(Vector2I.Zero);
    (Direction.Left.ToVector2I() + Direction.Right.ToVector2I()).ShouldBe(Vector2I.Zero);
    (Direction.UpLeft.ToVector2I() + Direction.DownRight.ToVector2I()).ShouldBe(Vector2I.Zero);
    (Direction.UpRight.ToVector2I() + Direction.DownLeft.ToVector2I()).ShouldBe(Vector2I.Zero);
  }

  [Test]
  public void TestHorizontalAndVertical() {
    WrapperDirection.Horizontal.ShouldBe(new[] { new Vector2I(-1, 0), new Vector2I(1, 0) });
    WrapperDirection.Vertical.ShouldBe(new[] { new Vector2I(0, -1), new Vector2I(0, 1) });

    // the two sets must not overlap, bridges rely on them being mutually exclusive
    WrapperDirection.Horizontal.Intersect(WrapperDirection.Vertical).ShouldBeEmpty();
  }

  [Test]
  public void TestEveryNeighborIsReachable() {
    // every tile around the origin must be covered exactly once
    var origin = new Vector2I(0, 0);
    var neighbors = WrapperDirection.Directions.Select(direction => origin + direction).ToList();

    for (var x = -1; x <= 1; x++) {
      for (var y = -1; y <= 1; y++) {
        var expected = new Vector2I(x, y);
        neighbors.Count(neighbor => neighbor == expected).ShouldBe(expected == origin ? 0 : 1);
      }
    }
  }
}
