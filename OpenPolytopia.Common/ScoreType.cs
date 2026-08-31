namespace OpenPolytopia.Common;

/// <summary>
/// Discriminates which kind of scoring event a <see cref="ScoreType"/> represents
/// </summary>
public enum ScoreTypeKind {
  /// <summary>A tile was revealed for the first time</summary>
  DiscoveredTile,

  /// <summary>A tile was claimed by a city</summary>
  ClaimedTile,

  /// <summary>Troops were spawned; carries how many and how many stars each costed</summary>
  TroopSpawned,

  /// <summary>A city leveled up; carries how many levels it gained</summary>
  CityLevelUp,

  /// <summary>A village was conquered</summary>
  VillageConquered,

  /// <summary>A temple was built</summary>
  TemplesBuilt,

  /// <summary>A new technology was researched</summary>
  DiscoveredNewTech,

  /// <summary>A park was built</summary>
  ParkBuilt,

  /// <summary>A monument was built</summary>
  MonumentsBuilt,

  /// <summary>A temple was destroyed</summary>
  TemplesDestroyed,

  /// <summary>A troop was lost; carries how many stars that troop costed</summary>
  LoseTroop,

  /// <summary>A park was destroyed</summary>
  ParkDestroyed,

  /// <summary>A monument was destroyed</summary>
  MonumentsDestroyed,

  /// <summary>A city was lost; carries the level the city was at</summary>
  LoseCity
}

/// <summary>
/// A single scoring event and the points it is worth.
/// </summary>
/// <remarks>
/// Instances are created through the static members below, never directly: the parameterless
/// cases are ready-made values (<see cref="ClaimedTile"/>) and the cases carrying data are
/// factory methods (<see cref="LoseCity"/>). Call <see cref="ToInt"/> to get the points.
/// </remarks>
public readonly record struct ScoreType {
  /// <summary>
  /// Which scoring event this instance represents
  /// </summary>
  public ScoreTypeKind Kind { get; }

  /// <summary>
  /// A count whose meaning depends on <see cref="Kind"/>: spawned troops, gained city levels
  /// or the level of a lost city. Zero for the cases that carry no count.
  /// </summary>
  private readonly uint _amount;

  /// <summary>
  /// The star cost of a single troop, for the troop-related cases. Zero otherwise.
  /// </summary>
  private readonly uint _stars;

  private ScoreType(ScoreTypeKind kind, uint amount = 0, uint stars = 0) {
    Kind = kind;
    _amount = amount;
    _stars = stars;
  }

  /// <summary>A tile was revealed for the first time</summary>
  public static readonly ScoreType DiscoveredTile = new(ScoreTypeKind.DiscoveredTile);

  /// <summary>A tile was claimed by a city</summary>
  public static readonly ScoreType ClaimedTile = new(ScoreTypeKind.ClaimedTile);

  /// <summary>A village was conquered</summary>
  public static readonly ScoreType VillageConquered = new(ScoreTypeKind.VillageConquered);

  /// <summary>A temple was built</summary>
  public static readonly ScoreType TemplesBuilt = new(ScoreTypeKind.TemplesBuilt);

  /// <summary>A new technology was researched</summary>
  public static readonly ScoreType DiscoveredNewTech = new(ScoreTypeKind.DiscoveredNewTech);

  /// <summary>A park was built</summary>
  public static readonly ScoreType ParkBuilt = new(ScoreTypeKind.ParkBuilt);

  /// <summary>A monument was built</summary>
  public static readonly ScoreType MonumentsBuilt = new(ScoreTypeKind.MonumentsBuilt);

  /// <summary>A temple was destroyed</summary>
  public static readonly ScoreType TemplesDestroyed = new(ScoreTypeKind.TemplesDestroyed);

  /// <summary>A park was destroyed</summary>
  public static readonly ScoreType ParkDestroyed = new(ScoreTypeKind.ParkDestroyed);

  /// <summary>A monument was destroyed</summary>
  public static readonly ScoreType MonumentsDestroyed = new(ScoreTypeKind.MonumentsDestroyed);

  /// <summary>
  /// Troops were spawned
  /// </summary>
  /// <param name="number">how many troops were spawned</param>
  /// <param name="stars">how many stars a single one of them costed</param>
  /// <returns>the corresponding <see cref="ScoreType"/></returns>
  public static ScoreType TroopSpawned(uint number, uint stars) =>
    new(ScoreTypeKind.TroopSpawned, number, stars);

  /// <summary>
  /// A city leveled up
  /// </summary>
  /// <param name="numberOfLevels">how many levels the city gained</param>
  /// <returns>the corresponding <see cref="ScoreType"/></returns>
  public static ScoreType CityLevelUp(uint numberOfLevels) =>
    new(ScoreTypeKind.CityLevelUp, numberOfLevels);

  /// <summary>
  /// A troop was lost
  /// </summary>
  /// <param name="stars">how many stars the lost troop costed</param>
  /// <returns>the corresponding <see cref="ScoreType"/></returns>
  public static ScoreType LoseTroop(uint stars) => new(ScoreTypeKind.LoseTroop, stars: stars);

  /// <summary>
  /// A city was lost
  /// </summary>
  /// <param name="cityLevel">the level the city was at</param>
  /// <returns>the corresponding <see cref="ScoreType"/></returns>
  public static ScoreType LoseCity(uint cityLevel) => new(ScoreTypeKind.LoseCity, cityLevel);

  /// <summary>
  /// How many points this scoring event is worth; negative when something was lost
  /// </summary>
  /// <returns>the points to add to a <see cref="Score"/></returns>
  /// <exception cref="ArgumentOutOfRangeException">if <see cref="Kind"/> isn't a known case</exception>
  public int ToInt() => Kind switch {
    ScoreTypeKind.DiscoveredTile => 5,
    ScoreTypeKind.ClaimedTile => 20,
    ScoreTypeKind.TroopSpawned => (int)(_amount * ((_stars * 5) + 5)),
    ScoreTypeKind.CityLevelUp => (int)(50 * _amount),
    ScoreTypeKind.VillageConquered or ScoreTypeKind.TemplesBuilt or ScoreTypeKind.DiscoveredNewTech => 100,
    ScoreTypeKind.ParkBuilt => 200,
    ScoreTypeKind.MonumentsBuilt => 400,
    ScoreTypeKind.TemplesDestroyed => -TemplesBuilt.ToInt(),
    ScoreTypeKind.LoseTroop => -TroopSpawned(1, _stars).ToInt(),
    ScoreTypeKind.ParkDestroyed => -ParkBuilt.ToInt(),
    ScoreTypeKind.MonumentsDestroyed => -MonumentsBuilt.ToInt(),
    ScoreTypeKind.LoseCity => -(VillageConquered.ToInt() + CityLevelUp(_amount).ToInt()),
    _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown score type")
  };
}
