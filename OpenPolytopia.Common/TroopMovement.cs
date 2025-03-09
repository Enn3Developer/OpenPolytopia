namespace OpenPolytopia.Common;

using System.Collections;
using Godot;

/// <summary>
///
/// </summary>
/// <param name="skills"></param>
/// <param name="tile"></param>
/// <returns></returns>
public static class SkillsExtension {
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
  /// <summary>
  /// Search possible path a troop could do.
  /// </summary>
  /// <param name="troopPosition">Position of selected troop</param>
  /// <returns></returns>
  public async IAsyncEnumerable<Vector2I> DiscoverPath(Vector2I troopPosition) {
    var troopData = troopManager[troopPosition];
    if (troopData.IsValid()) {
      var troop = troopManager[troopData.Type];
      // Depth searching
      var queue = new List<WrapperMovement>();
      queue.Add(new WrapperMovement { Position = troopPosition });
      var index = 0;
      while (index < queue.Count) {
        var currentPos = queue[index];
        currentPos.CurrentMarker = WrapperMovement.Marker.Gray;
        if (currentPos.Depth > troop.Movement) {
          index++;
          continue;
        }

        for (var offsetX = -1; offsetX <= 1; offsetX++) {
          var nx = troopPosition.X + offsetX;
          if (nx < 0) {
            continue;
          }

          for (var offsetY = -1; offsetY <= 1; offsetY++) {
            if (offsetX == 0 && offsetY == 0) {
              continue;
            }

            var ny = troopPosition.Y + offsetY;
            if (ny < 0) {
              continue;
            }

            var checkPos = queue.Find(wrapperMovement =>
              wrapperMovement.Position.X == nx && wrapperMovement.Position.Y == ny);
            if (checkPos == null) {
              queue.Add(new WrapperMovement { Position = new Vector2I(nx, ny), Depth = currentPos.Depth + 1 });
            }
          }
        }

        if (currentPos.Position != troopPosition && !troopManager[currentPos.Position].IsValid() &&
            troop.Skills.CheckForTile(gameGrid[currentPos.Position])) {
          yield return currentPos.Position;
        }

        currentPos.CurrentMarker = WrapperMovement.Marker.Black;
        index++;
      }
    }
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
