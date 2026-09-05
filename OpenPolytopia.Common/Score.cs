namespace OpenPolytopia.Common;

using System.Runtime.CompilerServices;

/// <summary>
/// Score for a single player
/// </summary>
public class Score {
  private int _value;

  public int ScoreValue {
    get => _value;
    private set => _value = value < 0 ? 0 : value;
  }

  /// <summary>
  /// Add to score of player
  /// </summary>
  /// <param name="type">the scoring event to add, see <see cref="ScoreType"/></param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AddScore(ScoreType type) => ScoreValue += type.ToInt();

  /// <summary>
  /// Overwrites the score with the one of a persisted game
  /// </summary>
  /// <remarks>
  /// Only <see cref="Gameplay.Game.Restore"/> uses this: a score is otherwise only ever moved by
  /// <see cref="AddScore"/>, so every point a player has comes from a scoring event
  /// </remarks>
  /// <param name="value">the score to restore; clamped to 0 like every other write</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void Restore(int value) => ScoreValue = value;
}
