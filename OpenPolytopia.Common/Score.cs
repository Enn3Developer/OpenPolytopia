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
}
