namespace OpenPolytopia.Common;

using Godot;

/// <summary>
/// Represents cardinal and diagonal directions for grid-based movement
/// </summary>
public enum Direction {
  Up, Down, Left, Right,
  UpLeft, UpRight, DownLeft, DownRight
}

/// <summary>
/// Provides precomputed direction vectors and direction-related utilities
/// for grid-based movement systems
/// </summary>
public static class WrapperDirection {

  /// <summary>
  /// Primary direction mapping between Direction enum values and their counterparts
  /// </summary>
  /// <remarks>
  /// Contains tuples pairing each Direction with its Vector2I equivalent.
  /// Used as the source for all direction-related vector data in the class.
  /// </remarks>
  public static readonly (Direction Direction, Vector2I Position)[] Directions = [
    (Direction.Up, new(0, -1)),
    (Direction.Down, new(0, 1)),
    (Direction.Left, new(-1, 0)),
    (Direction.Right, new(1, 0)),
    (Direction.UpLeft, new(-1, -1)),
    (Direction.UpRight, new(1, -1)),
    (Direction.DownLeft, new(-1, -1)),
    (Direction.DownRight, new(1, 1))
  ];

  /// <summary>
  /// Contains all movement vectors for full 8-directional movement
  /// </summary>
  /// <remarks>
  /// Precomputed array of Vector2I values extracted from Directions.
  /// Use this for iterations requiring all possible movement directions.
  /// </remarks>
  public static readonly Vector2I[] AllDirections = Directions.Select(d => d.Position).ToArray();

  /// <summary>
  /// Left and right movement vectors for horizontal constraints
  /// </summary>
  /// <remarks>
  /// Contains vectors: (-1, 0) and (1, 0)
  /// </remarks>
  public static readonly Vector2I[] Horizontal = [
    Directions.First(d => d.Direction == Direction.Left).Position,
    Directions.First(d => d.Direction == Direction.Right).Position
  ];

  /// <summary>
  /// Up and down movement vectors for vertical constraints
  /// </summary>
  /// <remarks>
  /// Contains vectors: (0, 1) and (0, -1)
  /// </remarks>
  public static readonly Vector2I[] Vertical = [
    Directions.First(d => d.Direction == Direction.Up).Position,
    Directions.First(d => d.Direction == Direction.Down).Position
  ];
}
