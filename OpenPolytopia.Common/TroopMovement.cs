namespace OpenPolytopia.Common;

using Godot;

public static class SkillsExtension {
  /// <summary>
  /// Check if troop have the skill for the tile
  /// </summary>
  /// <param name="skills">Skills of the troop</param>
  /// <param name="tile">Tile where troop could move</param>
  /// <returns>Is troop have skill for tile</returns>
  public static bool CheckForTile(this Skill[] skills, Tile tile) => TroopMovement.IsGround(tile)
    ? skills.All(skill => skill != Skill.Water)
    : skills.Any(skill => skill is Skill.Water or Skill.Fly);
}

/// <summary>
/// Wrapper of depth first search
/// </summary>
internal class WrapperMovement {
  public uint Depth { get; set; }
  public Vector2I Position { get; init; }
}

public class TroopMovement(TroopManager troopManager, Grid gameGrid) {
  /// <summary>
  /// Search possible path a troop could do.
  /// Considering:
  /// - Double movement range on roads
  /// - Bridge movement constraints
  /// - Terrain skills
  /// </summary>
  /// <param name="troopPosition">Position of selected troop</param>
  /// <returns>Async sequence of reachable positions</returns>
  public async IAsyncEnumerable<Vector2I> DiscoverPathAsync(Vector2I troopPosition) {
    if (!troopManager[troopPosition].IsValid()) {
      yield break;
    }

    var troop = troopManager[troopManager[troopPosition].Type];
    var range = troop.Movement * 2;
    // Depth first searching
    var queue = new List<WrapperMovement> { new() { Position = troopPosition } };
    var index = 0;
    while (index < queue.Count) {
      await Task.Yield();
      var currentPos = queue[index];
      if (currentPos.Depth > range) {
        index++;
        continue;
      }

      foreach (var dir in WrapperDirection.AllDirections) {
        var neighbors = currentPos.Position + dir;
        var existing = queue.Find(movement => movement.Position == neighbors);
        var newDepth = currentPos.Depth + (gameGrid[currentPos.Position].Roads ? 1u : 2u);
        if (existing != null || !isValidNeighbor(neighbors)) {
          continue;
        }

        if (gameGrid[neighbors].Roads && gameGrid[neighbors].Kind == TileKind.Water) {
          var bridgeData = gameGrid[neighbors].GetCustomData<BridgeData>();
          switch (bridgeData.Direction) {
            case BridgeDirection.Vertical when WrapperDirection.Horizontal.Contains(dir):
            case BridgeDirection.Horizontal when WrapperDirection.Vertical.Contains(dir):
              continue;
          }
        }
        queue.Add(new WrapperMovement { Position = neighbors, Depth = newDepth });
      }

      if (shouldYieldPosition(currentPos.Position, troopPosition)) {
        yield return currentPos.Position;
      }

      index++;
    }

    bool isValidNeighbor(Vector2I pos) => pos is { X: >= 0, Y: >= 0 } && pos.X < gameGrid.Size && pos.Y < gameGrid.Size;

    bool shouldYieldPosition(Vector2I currentPos, Vector2I startPos) =>
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
