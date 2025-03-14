namespace OpenPolytopia.Common;

using System.Runtime.Serialization;
using System.Text.Json.Serialization;

public class TribeManager {
  private readonly Dictionary<TribeType, Tribe> _tribes = new(8);

  public Tribe? this[TribeType type] => _tribes.GetValueOrDefault(type);

  public void RegisterTribe(TribeType type, Tribe tribe) => _tribes.Add(type, tribe);

  public void RegisterTribes(TribesSerializedData tribes) {
    foreach (var tribe in tribes.Tribes) {
      RegisterTribe(tribe.TribeType, tribe.Tribe);
    }
  }
}

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
[JsonConverter(typeof(JsonStringEnumConverter<TribeType>))]
public enum TribeType {
  [EnumMember(Value = "imperius")] Imperius = 0,
  [EnumMember(Value = "bardur")] Bardur = 1,
  [EnumMember(Value = "oumaji")] Oumaji = 2,
  [EnumMember(Value = "kickoo")] Kickoo = 3,
  [EnumMember(Value = "vengir")] Vengir = 4,
  [EnumMember(Value = "elyrion")] Elyrion = 5,
}

/// <summary>
/// Enum containing all resource types
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResourceType>))]
public enum ResourceType {
  [EnumMember(Value = "none")] None,
  [EnumMember(Value = "fruit")] Fruit,
  [EnumMember(Value = "crop")] Crop,
  [EnumMember(Value = "animal")] Animal,
  [EnumMember(Value = "fish")] Fish,
  [EnumMember(Value = "mineral")] Mineral,
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
  EyeOfGod = 7
}

public class TribeSerializedData {
  public required TribeType TribeType { get; init; }
  public required Tribe Tribe { get; init; }
}

public class TribesSerializedData {
  public required List<TribeSerializedData> Tribes { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Tribe))]
[JsonSerializable(typeof(TribeSerializedData))]
[JsonSerializable(typeof(TribesSerializedData))]
public partial class TribeGenerationContext : JsonSerializerContext;
