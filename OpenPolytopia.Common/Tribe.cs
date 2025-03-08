namespace OpenPolytopia.Common;

public class Tribe {
  public required StartingTech StartingTech { get; init; }
  public required SpawnRate SpawnRate { get; init; }
  public required TerrainRate TerrainRate { get; init; }
}

/// <summary>
/// Spawn rates for every tribe
/// </summary>
/// <remarks>
/// For every field there's the initial value, these rates are multipliers
/// </remarks>
public class SpawnRate {
  /// <summary>
  /// Initial value = 0.18
  /// </summary>
  public required float FruitRate { get; init; }

  /// <summary>
  /// Initial value = 0.18
  /// </summary>
  public required float CropRate { get; init; }

  /// <summary>
  /// Initial value = 0.19
  /// </summary>
  public required float AnimalRate { get; init; }

  /// <summary>
  /// Initial value = 0.5
  /// </summary>
  public required float FishRate { get; init; }

  /// <summary>
  /// Initial value = 0.11
  /// </summary>
  public required float MineralRate { get; init; }
}

/// <summary>
/// Terrain rates for every tribe
/// </summary>
/// <remarks>
/// For every field there's the initial value, these rates are multipliers
/// </remarks>
public class TerrainRate {
  /// <summary>
  /// Initial value = 0.38
  /// </summary>
  public required float ForestRate { get; init; }

  /// <summary>
  /// Initial value = 0.14
  /// </summary>
  public required float MountainRate { get; init; }

  /// <summary>
  /// No initial value because it depends on the map type
  /// </summary>
  public required float WaterRate { get; init; }
}

/// <summary>
/// Tribes enum
/// </summary>
/// <remarks>
/// There can only be max 32 tribes
/// </remarks>
public enum TribeType {
  Imperius = 0,
  Bardur = 1,
  Oumaji = 2,
  Kickoo = 3,
  Vengir = 4,
  Elyrion = 5,
}

public enum ResourceType {
  None,
  Fruit,
  Crop,
  Animal,
  Fish,
  Mineral,
}

/// <summary>
/// Available wonders to build
/// </summary>
/// <remarks>
/// At the moment, only 7 wonders max can be built.
/// <br/>
/// Because this is used internally in <see cref="Tile"/> there is a null option at index 0 (<see cref="Wonder.None"/>)
/// </remarks>
public enum Wonder {
  None = 0,
  ParkOfFortune = 1,
  AltarOfPeace = 2,
  TowerOfWisdom = 3,
  GrandBazaar = 4,
  GateOfPower = 5,
  EmperorsTomb = 6,
  EyeTower = 7,
}
