namespace OpenPolytopia.Common;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Computes where a troop can move to on the grid
/// </summary>
/// <param name="troopManager">the troops on <paramref name="gameGrid"/></param>
/// <param name="gameGrid">the grid to move on</param>
public class TroopMovement(TroopManager troopManager, Grid gameGrid) {
  /// <summary>
  /// Every tile the troop can end its movement on, following the movement rules
  /// </summary>
  /// <remarks>
  /// Dijkstra over integer step costs: the budget is <c>Troop.Movement * 2</c>, a step costs 1 when both the tile
  /// left and the tile entered have a road (<see cref="IsRoad"/>), 2 otherwise. Occupied tiles are never entered
  /// nor expanded from. Forest, mountain (unless the troop can <see cref="Skill.Fly"/>) and any tile 8-adjacent to
  /// an enemy troop are terminal: the troop can stop there but the search doesn't continue past them. The starting
  /// tile is always expanded, even under a zone of control.
  /// </remarks>
  /// <param name="troopPosition">the position of the troop to move</param>
  /// <param name="canClimb">whether the troop's player has researched <see cref="TechIds.CLIMBING"/></param>
  /// <returns>every tile the troop can legally stop on; empty if there's no valid troop at <paramref name="troopPosition"/></returns>
  public HashSet<Vector2I> ReachableTiles(Vector2I troopPosition, bool canClimb = false) {
    var reachable = new HashSet<Vector2I>();
    var startData = troopManager[troopPosition];
    if (!startData.IsValid()) {
      return reachable;
    }

    var troop = troopManager[startData.Type];
    var mover = startData.Player;
    var budget = troop.Movement * 2;

    var bestCost = new Dictionary<Vector2I, uint> { [troopPosition] = 0 };
    var frontier = new PriorityQueue<Vector2I, uint>();
    frontier.Enqueue(troopPosition, 0);

    while (frontier.TryDequeue(out var current, out var cost)) {
      // a cheaper path to this tile was already processed; this entry is stale
      if (cost > bestCost[current]) {
        continue;
      }

      var isStart = current == troopPosition;
      if (!isStart) {
        reachable.Add(current);

        if (IsTerminal(current, troop, mover)) {
          continue;
        }
      }

      var leavingRoad = IsRoad(gameGrid[current]);
      foreach (var direction in WrapperDirection.Directions) {
        var next = current + direction;
        if (!IsInsideGrid(next) || troopManager[next].IsValid()) {
          continue;
        }

        var nextTile = gameGrid[next];
        if (!troop.Skills.CanEnter(nextTile, canClimb) || !CanCrossInto(nextTile, direction)) {
          continue;
        }

        var stepCost = leavingRoad && IsRoad(nextTile) ? 1u : 2u;
        var newCost = cost + stepCost;
        if (newCost > budget) {
          continue;
        }

        if (bestCost.TryGetValue(next, out var known) && known <= newCost) {
          continue;
        }

        bestCost[next] = newCost;
        frontier.Enqueue(next, newCost);
      }
    }

    return reachable;
  }

  /// <summary>
  /// Thin wrapper over <see cref="ReachableTiles"/> that yields the reachable tiles asynchronously
  /// </summary>
  /// <param name="troopPosition">the position of the troop to move</param>
  /// <returns>the same tiles as <see cref="ReachableTiles"/>, one at a time</returns>
  public async IAsyncEnumerable<Vector2I> DiscoverPathAsync(Vector2I troopPosition) {
    await Task.Yield();
    foreach (var position in ReachableTiles(troopPosition)) {
      yield return position;
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

  /// <summary>
  /// Whether a tile has a road, for movement cost purposes
  /// </summary>
  /// <param name="tile">the tile to check</param>
  /// <returns>true if the tile has <see cref="Tile.Roads"/> set or is a city tile</returns>
  public static bool IsRoad(Tile tile) =>
    tile.Roads || (tile.Kind == TileKind.Village && tile.Modifier == (int)VillageTileModifier.City);

  /// <summary>
  /// Whether a tile can stop a moving troop but never lets it move further
  /// </summary>
  /// <param name="position">the tile to check</param>
  /// <param name="troop">the moving troop's definition</param>
  /// <param name="mover">the id of the player moving the troop</param>
  /// <returns>true if the tile is forest, a mountain (unless the troop can fly), or 8-adjacent to an enemy troop</returns>
  private bool IsTerminal(Vector2I position, Troop troop, uint mover) {
    var tile = gameGrid[position];
    if (tile.Kind == TileKind.Forest) {
      return true;
    }

    if (tile.Kind == TileKind.Mountain && !troop.Skills.HasSkill(Skill.Fly)) {
      return true;
    }

    return IsAdjacentToEnemy(position, mover);
  }

  /// <summary>
  /// Whether any of a tile's 8 neighbors holds a troop of another player, the movement zone of control
  /// </summary>
  /// <param name="position">the tile to check around</param>
  /// <param name="mover">the id of the player moving the troop</param>
  /// <returns>true if an enemy troop stands adjacent to <paramref name="position"/></returns>
  private bool IsAdjacentToEnemy(Vector2I position, uint mover) {
    foreach (var direction in WrapperDirection.Directions) {
      var neighbor = position + direction;
      if (!IsInsideGrid(neighbor)) {
        continue;
      }

      var neighborTroop = troopManager[neighbor];
      if (neighborTroop.IsValid() && neighborTroop.Player != mover) {
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// Whether a tile is inside the bounds of <c>gameGrid</c>
  /// </summary>
  /// <param name="position">the position to check</param>
  /// <returns>true if both coordinates are within <c>[0, gameGrid.Size)</c></returns>
  private bool IsInsideGrid(Vector2I position) =>
    position.X >= 0 && position.X < gameGrid.Size && position.Y >= 0 && position.Y < gameGrid.Size;

  /// <summary>
  /// Whether a bridge, if the target tile is one, can be entered from the given direction
  /// </summary>
  /// <remarks>
  /// A bridge (a road on water) can only be crossed along its <see cref="BridgeData.Direction"/>: a vertical
  /// bridge refuses a horizontal step into it and vice versa
  /// </remarks>
  /// <param name="tile">the tile being entered</param>
  /// <param name="direction">the direction of the step into <paramref name="tile"/></param>
  /// <returns>true if the step is allowed</returns>
  private static bool CanCrossInto(Tile tile, Vector2I direction) {
    if (!tile.Roads || tile.Kind != TileKind.Water) {
      return true;
    }

    var bridgeData = tile.GetCustomData<BridgeData>();
    return bridgeData.Direction switch {
      BridgeDirection.Vertical when WrapperDirection.Horizontal.Contains(direction) => false,
      BridgeDirection.Horizontal when WrapperDirection.Vertical.Contains(direction) => false,
      _ => true
    };
  }
}
