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
  /// Primary direction vector values
  /// </summary>
  /// <remarks>
  /// Used as the source for all direction-related vector data in the class.
  /// </remarks>
  public static readonly Vector2I[] Directions = [
    new(0, -1),
    new(0, 1),
    new(-1, 0),
    new(1, 0),
    new(-1, -1),
    new(1, -1),
    new(-1, 1),
    new(1, 1)
  ];

  /// <summary>
  /// Movement vector of a single direction
  /// </summary>
  /// <param name="direction">the direction to convert</param>
  /// <returns>the matching entry of <see cref="Directions"/></returns>
  public static Vector2I ToVector2I(this Direction direction) => Directions[(int)direction];

  /// <summary>
  /// Left and right movement vectors for horizontal constraints
  /// </summary>
  /// <remarks>
  /// Contains vectors: (-1, 0) and (1, 0)
  /// </remarks>
  public static readonly Vector2I[] Horizontal = [
    Direction.Left.ToVector2I(),
    Direction.Right.ToVector2I()
  ];

  /// <summary>
  /// Up and down movement vectors for vertical constraints
  /// </summary>
  /// <remarks>
  /// Contains vectors: (0, -1) and (0, 1)
  /// </remarks>
  public static readonly Vector2I[] Vertical = [
    Direction.Up.ToVector2I(),
    Direction.Down.ToVector2I()
  ];
}
