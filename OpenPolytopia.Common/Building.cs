namespace OpenPolytopia.Common;

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

/// <summary>
/// Types of buildings and harvest actions that can be built on a tile
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter<BuildingType>))]
public enum BuildingType {
  /// <summary>Harvests fruit on a field tile, consuming the resource</summary>
  [EnumMember(Value = "fruit")] Fruit,

  /// <summary>Harvests animals on a forest tile, consuming the resource</summary>
  [EnumMember(Value = "hunting")] Hunting,

  /// <summary>Harvests fish on a water/ocean tile, consuming the resource</summary>
  [EnumMember(Value = "fishing")] Fishing,

  /// <summary>Farm built on a field tile with a crop resource</summary>
  [EnumMember(Value = "farm")] Farm,

  /// <summary>Mine built on a mountain tile with a mineral resource</summary>
  [EnumMember(Value = "mine")] Mine,

  /// <summary>Lumber hut built on a forest tile</summary>
  [EnumMember(Value = "lumberHut")] LumberHut,

  /// <summary>Port built on a water tile</summary>
  [EnumMember(Value = "port")] Port,

  /// <summary>Windmill built on a field tile, gains population from adjacent farms</summary>
  [EnumMember(Value = "windmill")] Windmill,

  /// <summary>Sawmill built on a field tile, gains population from adjacent lumber huts</summary>
  [EnumMember(Value = "sawmill")] Sawmill,

  /// <summary>Forge built on a field tile, gains population from adjacent mines</summary>
  [EnumMember(Value = "forge")] Forge,

  /// <summary>Temple built on a field tile</summary>
  [EnumMember(Value = "temple")] Temple,

  /// <summary>Temple built on a forest tile</summary>
  [EnumMember(Value = "forestTemple")] ForestTemple,

  /// <summary>Temple built on a mountain tile</summary>
  [EnumMember(Value = "mountainTemple")] MountainTemple,

  /// <summary>Temple built on a water tile</summary>
  [EnumMember(Value = "waterTemple")] WaterTemple,

  /// <summary>Road built on a field or forest tile</summary>
  [EnumMember(Value = "road")] Road
}

/// <summary>
/// Static definition of a building or a harvest action
/// </summary>
public class Building {
  /// <summary>
  /// The kind of tile this building goes on
  /// </summary>
  /// <remarks>
  /// For <see cref="BuildingType.Road"/> this is <see cref="TileKind.Field"/>, but the rules also allow roads on
  /// <see cref="TileKind.Forest"/>
  /// </remarks>
  public required TileKind Kind { get; init; }

  /// <summary>
  /// The value stored in <see cref="Tile.Building"/> when this is built
  /// </summary>
  /// <remarks>
  /// 0 for harvest actions (fruit, hunting, fishing) and for roads, which don't occupy <see cref="Tile.Building"/>
  /// </remarks>
  public required int TileBuilding { get; init; }

  /// <summary>
  /// Stars needed to build it
  /// </summary>
  public required uint Cost { get; init; }

  /// <summary>
  /// Id of the tech that unlocks it; null when no tech is needed
  /// </summary>
  public string? RequiredTech { get; init; }

  /// <summary>
  /// Base population given to the city when this is built
  /// </summary>
  public required uint Population { get; init; }

  /// <summary>
  /// The resource the tile must have for this to be built; <see cref="ResourceType.None"/> when none is needed
  /// </summary>
  public ResourceType RequiresResource { get; init; }

  /// <summary>
  /// Whether building this clears the tile's resource modifier
  /// </summary>
  public bool ConsumesResource { get; init; }

  /// <summary>
  /// Whether only one tile of a city may have this building
  /// </summary>
  public bool UniquePerCity { get; init; }

  /// <summary>
  /// The building whose 8-neighbor adjacency gives extra population to this building's city
  /// </summary>
  public BuildingType? AdjacentTo { get; init; }

  /// <summary>
  /// Population given to the city per adjacent <see cref="AdjacentTo"/> building
  /// </summary>
  public uint AdjacentPopulation { get; init; }

  /// <summary>
  /// Whether this building is a temple, for <see cref="ScoreType.TemplesBuilt"/> scoring
  /// </summary>
  public bool IsTemple { get; init; }
}

/// <summary>
/// A building entry as deserialized from <c>buildings.json</c>
/// </summary>
public class BuildingSerializedData {
  /// <summary>
  /// The building type
  /// </summary>
  public required BuildingType Type { get; init; }

  /// <summary>
  /// The building definition
  /// </summary>
  public required Building Building { get; init; }
}

/// <summary>
/// All the buildings deserialized from <c>buildings.json</c>
/// </summary>
public class BuildingsSerializedData {
  /// <summary>
  /// The buildings
  /// </summary>
  public required List<BuildingSerializedData> Buildings { get; init; }
}

/// <summary>
/// Source generated (de)serialization context for <c>buildings.json</c>
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Building))]
[JsonSerializable(typeof(BuildingSerializedData))]
[JsonSerializable(typeof(BuildingsSerializedData))]
public partial class BuildingGenerationContext : JsonSerializerContext;

/// <summary>
/// Manages the registered <see cref="Building"/> definitions
/// </summary>
public class BuildingManager {
  private readonly Dictionary<BuildingType, Building> _buildings = new(16);

  /// <summary>
  /// Gets a building definition
  /// </summary>
  /// <param name="type">the building type</param>
  /// <returns>the building; null if not registered</returns>
  public Building? this[BuildingType type] => _buildings.GetValueOrDefault(type);

  /// <summary>
  /// Registers a building
  /// </summary>
  /// <param name="type">the building type</param>
  /// <param name="building">the building definition</param>
  /// <exception cref="ArgumentException">
  /// if <c>type</c> isn't a valid <see cref="BuildingType"/> or is already registered
  /// </exception>
  /// <exception cref="ArgumentNullException">if <c>building</c> is null</exception>
  public void RegisterBuilding(BuildingType type, Building building) {
    ArgumentNullException.ThrowIfNull(building);

    // the json converter of BuildingType accepts numbers too, so a type that doesn't exist can get this far
    if (!Enum.IsDefined(type)) {
      throw new ArgumentException($"{type} isn't a valid building type", nameof(type));
    }

    if (!_buildings.TryAdd(type, building)) {
      throw new ArgumentException($"building {type} is registered more than once", nameof(type));
    }
  }

  /// <summary>
  /// Registers multiple buildings
  /// </summary>
  /// <param name="data">the deserialized data of all buildings to register</param>
  /// <exception cref="ArgumentException">
  /// if the <see cref="BuildingSerializedData.Type"/> of a building isn't a valid <see cref="BuildingType"/> or a
  /// building is declared more than once
  /// </exception>
  /// <exception cref="ArgumentNullException">
  /// if <c>data</c>, the list of the buildings, a building or the definition of a building is null
  /// </exception>
  public void RegisterBuildings(BuildingsSerializedData data) {
    ArgumentNullException.ThrowIfNull(data);
    ArgumentNullException.ThrowIfNull(data.Buildings);

    foreach (var building in data.Buildings) {
      ArgumentNullException.ThrowIfNull(building);
      RegisterBuilding(building.Type, building.Building);
    }
  }

  /// <summary>
  /// Maps a resource to the tile kind and modifier value that carry it
  /// </summary>
  /// <param name="resource">the resource</param>
  /// <param name="kind">the tile kind that carries the resource</param>
  /// <param name="modifier">the modifier value on that tile kind, castable to its modifier enum</param>
  /// <returns>false if <c>resource</c> is <see cref="ResourceType.None"/> or isn't a valid <see cref="ResourceType"/></returns>
  /// <remarks>
  /// <see cref="ResourceType.Fish"/> maps to <see cref="TileKind.Water"/>, but fish also spawns on
  /// <see cref="TileKind.Ocean"/>; callers must accept both
  /// </remarks>
  public static bool TryGetResourceTile(ResourceType resource, out TileKind kind, out int modifier) {
    switch (resource) {
      case ResourceType.Fruit:
        kind = TileKind.Field;
        modifier = (int)FieldTileModifier.Fruit;
        return true;
      case ResourceType.Crop:
        kind = TileKind.Field;
        modifier = (int)FieldTileModifier.Crop;
        return true;
      case ResourceType.Animal:
        kind = TileKind.Forest;
        modifier = (int)ForestTileModifier.Animal;
        return true;
      case ResourceType.Fish:
        kind = TileKind.Water;
        modifier = (int)WaterTileModifier.Fish;
        return true;
      case ResourceType.Mineral:
        kind = TileKind.Mountain;
        modifier = (int)MountainTileModifier.Ore;
        return true;
      default:
        kind = default;
        modifier = 0;
        return false;
    }
  }
}
