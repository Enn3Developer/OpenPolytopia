namespace OpenPolytopia;

using System.Runtime.CompilerServices;

/// <summary>
/// Score for a single player
/// </summary>
public class Score {
  private int _value { get; set; }

  public int ScoreValue {
    get => _value;
    private set => _value = value < 0 ? 0 : value;
  }

  /// <summary>
  /// Add to score of player
  /// </summary>
  /// <remarks>
  /// <see cref="ScoreType.TroopSpawned"/> -> requires only one int > 1; This int is the cost in stars of the unit
  /// <br/>
  /// <see cref="ScoreType.CityLevelUp"/> -> require only one int > 0; This int is the level of the city
  /// <br/>
  /// <see cref="ScoreType.LoseCity"/> -> require only one int > 0; This int is the level of the city lost
  /// </remarks>
  /// <param name="type"> uint value of score <see cref="ScoreType"/></param>
  /// <param name="additionalParameters"> These are additional parameters that you can pass to the score calculation</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AddScore(ScoreType type, params object[] additionalParameters) => ScoreValue += ScoreValueToInt(type, additionalParameters);


  /// <summary>
  /// Convert key ScoreValue enum into a uint value
  /// </summary>
  private int ScoreValueToInt(ScoreType type, params object[] additionalParameters) {
    switch (type) {
      case ScoreType.DiscoveredTile:
        return 5;
      case ScoreType.TroopSpawned:
        if (additionalParameters.Length > 0 && additionalParameters[0] is int level) {
          return 5 + (5 * level);
        }
        return 0;
      case ScoreType.ClaimedTile:
        return 20;
      case ScoreType.CityLevelUp:
        if (additionalParameters.Length > 0 && additionalParameters[0] is int levelCity) {
          return 50 * levelCity;
        }

        return 0;
      case ScoreType.VillageConquered or ScoreType.TemplesBuilt or ScoreType.DiscoveredNewTech:
        return 100;
      case ScoreType.ParkBuilt:
        return 200;
      case ScoreType.MonumentsBuilt:
        return 400;
      case ScoreType.TemplesDestroyed:
        return -ScoreValueToInt(ScoreType.TemplesBuilt);
      case ScoreType.LoseTroop:
        return -ScoreValueToInt(ScoreType.TroopSpawned);
      case ScoreType.ParkDestroyed:
        return -ScoreValueToInt(ScoreType.ParkBuilt);
      case ScoreType.MonumentsDestroyed:
        return -ScoreValueToInt(ScoreType.MonumentsBuilt);
      case ScoreType.LoseCity:
        if (additionalParameters.Length > 0 && additionalParameters[0] is int cityLevel) {
          return -(ScoreValueToInt(ScoreType.VillageConquered) +
                 ScoreValueToInt(ScoreType.CityLevelUp, cityLevel));
        }
        return 0;
      default:
        return 0;
    }
  }
}

/// <summary>
/// Action / Assets Point Values enum
/// </summary>
public enum ScoreType {
  DiscoveredTile, // Discover new tiles from the fog
  ClaimedTile, // Tile in territory of your city
  TroopSpawned, // Troop spawned
  VillageConquered, // Conquered a village
  CityLevelUp, // Level up a city
  ParkBuilt, // Built a park
  DiscoveredNewTech, // New tech discovered
  MonumentsBuilt, // Built a new monuments
  TemplesBuilt, // Built a new temple
  TemplesDestroyed, // Destroyed a temple
  LoseTroop, // Loss a troop level 2
  LoseCity, // Lose a city
  ParkDestroyed, // Loss for park built
  MonumentsDestroyed, // Loss for monuments built
}
