namespace OpenPolytopia.Common.Gameplay;

/// <summary>
/// Settings a <see cref="Game"/> is created with
/// </summary>
public class GameSettings {
  /// <summary>
  /// Maximum number of turns before the game ends and the highest score wins
  /// </summary>
  /// <remarks>
  /// 0 means no limit, so the game only ends by domination (a single player remaining)
  /// </remarks>
  public uint MaxTurns { get; init; }
}
