namespace OpenPolytopia.Common;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;

public static class EmbeddedResources {
  // serialization conventions shared by all the json resources
  private static readonly JsonSerializerOptions _jsonOptions = new() {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All),
    TypeInfoResolver = JsonTypeInfoResolver.Combine(TribeGenerationContext.Default, TroopGenerationContext.Default,
      TechTreeGenerationContext.Default, BuildingGenerationContext.Default),
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    // optional lists (see Tribe.TechOverrides) are left out of the files instead of being written as null
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  /// <summary>
  /// Get the troops data from the json file
  /// </summary>
  public static string TroopsData => GetResource("OpenPolytopia.Common.resources.troops.json");

  /// <summary>
  /// Get the tribes data from the json file
  /// </summary>
  public static string TribesData => GetResource("OpenPolytopia.Common.resources.tribes.json");

  /// <summary>
  /// Get the tech tree data from the json file
  /// </summary>
  public static string TechTreeData => GetResource("OpenPolytopia.Common.resources.tech_tree.json");

  /// <summary>
  /// Get the buildings data from the json file
  /// </summary>
  public static string BuildingsData => GetResource("OpenPolytopia.Common.resources.buildings.json");

  /// <summary>
  /// Deserializes the troops from the embedded json file
  /// </summary>
  /// <returns>the troops data, or null if the json is empty</returns>
  public static TroopsSerializedData? LoadTroops() => ParseTroops(TroopsData);

  /// <summary>
  /// Deserializes the tribes from the embedded json file
  /// </summary>
  /// <returns>the tribes data, or null if the json is empty</returns>
  public static TribesSerializedData? LoadTribes() => ParseTribes(TribesData);

  /// <summary>
  /// Deserializes the tech tree from the embedded json file
  /// </summary>
  /// <returns>the tech tree data, or null if the json is empty</returns>
  public static TechTreeSerializedData? LoadTechTree() => ParseTechTree(TechTreeData);

  /// <summary>
  /// Deserializes the buildings from the embedded json file
  /// </summary>
  /// <returns>the buildings data, or null if the json is empty</returns>
  public static BuildingsSerializedData? LoadBuildings() => ParseBuildings(BuildingsData);

  /// <summary>
  /// Deserializes troops from a json document following the schema of <c>troops.json</c>
  /// </summary>
  /// <param name="json">the json document</param>
  /// <returns>the troops data, or null if the json is empty</returns>
  /// <exception cref="JsonException">threw when the json doesn't follow the schema</exception>
  public static TroopsSerializedData? ParseTroops(string json) =>
    JsonSerializer.Deserialize<TroopsSerializedData>(json, _jsonOptions);

  /// <summary>
  /// Deserializes tribes from a json document following the schema of <c>tribes.json</c>
  /// </summary>
  /// <param name="json">the json document</param>
  /// <returns>the tribes data, or null if the json is empty</returns>
  /// <exception cref="JsonException">threw when the json doesn't follow the schema</exception>
  public static TribesSerializedData? ParseTribes(string json) =>
    JsonSerializer.Deserialize<TribesSerializedData>(json, _jsonOptions);

  /// <summary>
  /// Deserializes a tech tree from a json document following the schema of <c>tech_tree.json</c>
  /// </summary>
  /// <param name="json">the json document</param>
  /// <returns>the tech tree data, or null if the json is empty</returns>
  /// <exception cref="JsonException">threw when the json doesn't follow the schema</exception>
  public static TechTreeSerializedData? ParseTechTree(string json) =>
    JsonSerializer.Deserialize<TechTreeSerializedData>(json, _jsonOptions);

  /// <summary>
  /// Deserializes buildings from a json document following the schema of <c>buildings.json</c>
  /// </summary>
  /// <param name="json">the json document</param>
  /// <returns>the buildings data, or null if the json is empty</returns>
  /// <exception cref="JsonException">threw when the json doesn't follow the schema</exception>
  public static BuildingsSerializedData? ParseBuildings(string json) =>
    JsonSerializer.Deserialize<BuildingsSerializedData>(json, _jsonOptions);

  /// <summary>
  /// Serializes troops to a json document following the schema of <c>troops.json</c>
  /// </summary>
  /// <param name="troops">the troops data</param>
  /// <returns>the indented json document</returns>
  public static string Serialize(TroopsSerializedData troops) => JsonSerializer.Serialize(troops, _jsonOptions);

  /// <summary>
  /// Serializes tribes to a json document following the schema of <c>tribes.json</c>
  /// </summary>
  /// <param name="tribes">the tribes data</param>
  /// <returns>the indented json document</returns>
  public static string Serialize(TribesSerializedData tribes) => JsonSerializer.Serialize(tribes, _jsonOptions);

  /// <summary>
  /// Serializes a tech tree to a json document following the schema of <c>tech_tree.json</c>
  /// </summary>
  /// <param name="techTree">the tech tree data</param>
  /// <returns>the indented json document</returns>
  public static string Serialize(TechTreeSerializedData techTree) =>
    JsonSerializer.Serialize(techTree, _jsonOptions);

  /// <summary>
  /// Serializes buildings to a json document following the schema of <c>buildings.json</c>
  /// </summary>
  /// <param name="buildings">the buildings data</param>
  /// <returns>the indented json document</returns>
  public static string Serialize(BuildingsSerializedData buildings) =>
    JsonSerializer.Serialize(buildings, _jsonOptions);

  /// <summary>
  /// Returns the content of an embedded resource
  /// </summary>
  /// <param name="name">the name of the resource
  /// </param>
  /// <returns>the content of the embedded resource as a string</returns>
  /// <exception cref="FileNotFoundException">threw when the resource doesn't exist with that name</exception>
  /// <example>
  /// <code>
  /// try {
  ///   var troopsContent = EmbeddedResources.GetResource("OpenPolytopia.Common.resources.troops.json");
  /// } catch (FileNotFoundException) {
  ///   Console.Error.WriteLine("Resource not found");
  /// }
  /// </code>
  /// </example>
  public static string GetResource(string name) {
    var assembly = Assembly.GetExecutingAssembly();
    using var stream = assembly.GetManifestResourceStream(name);
    if (stream == null) {
      throw new FileNotFoundException("Troops data not found");
    }

    using var streamReader = new StreamReader(stream);
    return streamReader.ReadToEnd();
  }
}
