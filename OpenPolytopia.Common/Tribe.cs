namespace OpenPolytopia.Common;

public class Tribe {
  public required StartingTech StartingTech { get; init; }
  public required SpawnRate SpawnRate { get; init; }
  public required TerrainRate TerrainRate { get; init; }
}

public class SpawnRate {
  public required float FruitRate { get; init; }
  public required float CropRate { get; init; }
  public required float AnimalRate { get; init; }
  public required float FishRate { get; init; }
  public required float MineralRate { get; init; }
}

public class TerrainRate {
  public required float ForestRate { get; init; }
  public required float MountainRate { get; init; }
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
