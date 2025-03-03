namespace OpenPolytopia.Common;

using System.Runtime.CompilerServices;
using FSharpLibrary;

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
  /// <param name="type"> uint value of score <see cref="ScoreTypeModule.ScoreType"/></param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AddScore(ScoreTypeModule.ScoreType type) =>
    ScoreValue += ScoreTypeModule.ScoreTypeToInt(type);
}
