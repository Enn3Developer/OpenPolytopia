namespace OpenPolytopia.Common;

using Godot;

public static class SkillsExtension {
  /// <summary>
  /// Check if troop have the skill for the tile
  /// </summary>
  /// <param name="skills">Skills of the troop</param>
  /// <param name="tile">Tile where troop could move</param>
  /// <returns></returns>
  public static bool CheckForTile(this Skill[] skills, Tile tile) => TroopMovement.IsGround(tile)
    ? skills.All(skill => skill != Skill.Water)
    : skills.Any(skill => skill is Skill.Water or Skill.Fly);
}

internal class WrapperMovement {
  public enum Marker {
    White,
    Black,
    Gray,
  }

  public uint Depth { get; set; }
  public Vector2I Position { get; init; }
  public Marker CurrentMarker { get; set; } = Marker.White;
}

public class TroopMovement(TroopManager troopManager, Grid gameGrid) {
  public static Vector2I[] DIRECTIONS = {
    new(-1, -1), new(-1, 0), new(-1, 1), new(0, -1), new(0, 1), new(1, -1), new(1, 0), new(1, 1)
  };

  /// <summary>
  /// Search possible path a troop could do.
  /// </summary>
  /// <param name="troopPosition">Position of selected troop</param>
  /// <returns></returns>
  public async IAsyncEnumerable<Vector2I> DiscoverPathAsync(Vector2I troopPosition) {
    if (!troopManager[troopPosition].IsValid()) {
      yield break;
    }

    var troop = troopManager[troopManager[troopPosition].Type];
    // Depth searching
    var queue = new Queue<WrapperMovement>();
    queue.Enqueue(new WrapperMovement {
      Position = troopPosition, CurrentMarker = WrapperMovement.Marker.Black, Depth = 0
    });
    while (queue.TryDequeue(out var current)) {
      await Task.Yield();
      current.CurrentMarker = WrapperMovement.Marker.Gray;
      if (current.Depth > troop.Movement) {
        continue;
      }

      foreach (var dir in DIRECTIONS) {
        var neighbors = current.Position + dir;

        var newDepth = current.Depth + 1;
        if (IsValidNeighbor(neighbors) &&
            current.CurrentMarker is WrapperMovement.Marker.Gray or WrapperMovement.Marker.Black &&
            newDepth <= troop.Movement) {
          queue.Enqueue(new WrapperMovement { Position = neighbors, Depth = newDepth });
        }
      }

      if (ShouldYieldPosition(current.Position, troopPosition)) {
        yield return current.Position;
      }
      else {
        current.CurrentMarker = WrapperMovement.Marker.Black;
      }
    }

    bool IsValidNeighbor(Vector2I pos) => pos.X >= 0 && pos.Y >= 0;

    bool ShouldYieldPosition(Vector2I currentPos, Vector2I startPos) =>
      currentPos != startPos &&
      !troopManager[currentPos].IsValid() &&
      troop.Skills.CheckForTile(gameGrid[currentPos]);
  }

  /// <summary>
  /// Check if tile is ground
  /// </summary>
  /// <param name="tile">Tile of the grid</param>
  /// <returns>True if tile is ground</returns>
  public static bool IsGround(Tile tile) => tile.Kind switch {
    TileKind.Field or TileKind.Forest or TileKind.Mountain or TileKind.Village => true,
    _ => false
  };
}
