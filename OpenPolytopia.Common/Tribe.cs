namespace OpenPolytopia.Common;

using System.Runtime.Serialization;
using System.Text.Json.Serialization;

/// <summary>
/// Class holding all data about the tribes
/// </summary>
public class TribeManager {
  /// <summary>
  /// All the tribe registered
  /// </summary>
  public Dictionary<TribeType, Tribe> Tribes { get; } = new(8);

  /// <summary>
  /// Get a tribe by its registering type
  /// </summary>
  /// <param name="type">the type of the tribe</param>
  public Tribe? this[TribeType type] => Tribes.GetValueOrDefault(type);

  /// <summary>
  /// Register a tribe
  /// </summary>
  /// <param name="type">the type of the tribe</param>
  /// <param name="tribe">the data of the tribe</param>
  /// <example>
  /// <code>
  /// var imperiusTribe = new Tribe {
  ///   // settings for this tribe
  /// };
  /// var tribeManager = new TribeManager();
  /// tribeManager.RegisterTribe(TribeType.Imperius, imperiusTribe);
  /// </code>
  /// </example>
  public void RegisterTribe(TribeType type, Tribe tribe) => Tribes.Add(type, tribe);

  /// <summary>
  /// Register multiple tribes
  /// </summary>
  /// <param name="tribes">the deserialized data of all tribes to register</param>
  public void RegisterTribes(TribesSerializedData tribes) {
    foreach (var tribe in tribes.Tribes) {
      RegisterTribe(tribe.TribeType, tribe.Tribe);
    }
  }
}

/// <summary>
/// Class representing all data a tribe has
/// </summary>
public class Tribe {
  /// <summary>
  /// The starting tech of a tribe
  /// </summary>
  public required StartingTech StartingTech { get; init; }

  /// <summary>
  /// The resource spawn rates of a tribe
  /// </summary>
  public required SpawnRate SpawnRate { get; init; }

  /// <summary>
  /// The terrain generation rates of a tribe
  /// </summary>
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
