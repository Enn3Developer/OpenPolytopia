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
  /// <exception cref="ArgumentException">
  /// if <see cref="Tribe.StartingBranch"/> isn't a valid <see cref="BranchType"/> or if the tribe is already
  /// registered
  /// </exception>
  /// <exception cref="ArgumentNullException">if <c>tribe</c> is null</exception>
  /// <example>
  /// <code>
  /// var imperiusTribe = new Tribe {
  ///   // settings for this tribe
  /// };
  /// var tribeManager = new TribeManager();
  /// tribeManager.RegisterTribe(TribeType.Imperius, imperiusTribe);
  /// </code>
  /// </example>
  public void RegisterTribe(TribeType type, Tribe tribe) {
    ArgumentNullException.ThrowIfNull(tribe);

    // same as the branches of the tech tree, the json converter accepts numbers so a branch that doesn't exist can
    // get this far; the overrides can't be checked here because they need the definition of the tech tree
    if (!Enum.IsDefined(tribe.StartingBranch)) {
      throw new ArgumentException($"tribe {type} starts on {tribe.StartingBranch}, which isn't a valid branch",
        nameof(tribe));
    }

    // Add says an item with the same key is already there, the tech tree says which branch is declared twice
    if (!Tribes.TryAdd(type, tribe)) {
      throw new ArgumentException($"tribe {type} is registered more than once", nameof(type));
    }
  }

  /// <summary>
  /// Register multiple tribes
  /// </summary>
  /// <param name="tribes">the deserialized data of all tribes to register</param>
  /// <exception cref="ArgumentException">
  /// if the <see cref="Tribe.StartingBranch"/> of a tribe isn't a valid <see cref="BranchType"/> or if a tribe is
  /// declared more than once
  /// </exception>
  /// <exception cref="ArgumentNullException">
  /// if <c>tribes</c>, the list of the tribes, a tribe or the data of a tribe is null
  /// </exception>
  public void RegisterTribes(TribesSerializedData tribes) {
    ArgumentNullException.ThrowIfNull(tribes);

    // required only checks that the json set the property, and a json null sets it just fine
    ArgumentNullException.ThrowIfNull(tribes.Tribes);

    foreach (var tribe in tribes.Tribes) {
      // a json null is an element of the list like any other, the data of the tribe is checked by RegisterTribe
      ArgumentNullException.ThrowIfNull(tribe);

      RegisterTribe(tribe.TribeType, tribe.Tribe);
    }
  }
}

/// <summary>
/// Class representing all data a tribe has
/// </summary>
public class Tribe {
  /// <summary>
  /// The branch a tribe starts with
  /// </summary>
  /// <remarks>
  /// A tribe always starts with the tier 0 node of this branch already researched, whether it's the default node or
  /// one of its <see cref="TechOverrides"/>
  /// </remarks>
  public required BranchType StartingBranch { get; init; }

  /// <summary>
  /// The nodes of the default tech tree this tribe replaces with its own
  /// </summary>
  /// <remarks>
  /// A tribe without unique techs doesn't need to declare anything, so this is null most of the times
  /// <br/>
  /// It can't default to an empty list because the source generated deserializer skips property initializers on
  /// types with required properties
  /// </remarks>
  public List<TechOverride>? TechOverrides { get; init; }

  /// <summary>
  /// The resource spawn rates of a tribe
  /// </summary>
  public required SpawnRate SpawnRate { get; init; }

  /// <summary>
  /// The terrain generation rates of a tribe
  /// </summary>
  public required TerrainRate TerrainRate { get; init; }

  /// <summary>
  /// The starting stars of a tribe
  /// </summary>
  public required int StartingStars { get; init; }
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
[JsonConverter(typeof(CamelCaseEnumConverter<TribeType>))]
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
[JsonConverter(typeof(CamelCaseEnumConverter<ResourceType>))]
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
