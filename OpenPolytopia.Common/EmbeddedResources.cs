namespace OpenPolytopia.Common;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;

public static class EmbeddedResources {
  // serialization conventions shared by all the json resources
  private static readonly JsonSerializerOptions _jsonOptions = new() {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All),
    TypeInfoResolver = JsonTypeInfoResolver.Combine(TribeGenerationContext.Default, TroopGenerationContext.Default),
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
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
  /// Deserializes the troops from the embedded json file
  /// </summary>
  /// <returns>the troops data, or null if the json is empty</returns>
  public static TroopsSerializedData? LoadTroops() =>
    JsonSerializer.Deserialize<TroopsSerializedData>(TroopsData, _jsonOptions);

  /// <summary>
  /// Deserializes the tribes from the embedded json file
  /// </summary>
  /// <returns>the tribes data, or null if the json is empty</returns>
  public static TribesSerializedData? LoadTribes() =>
    JsonSerializer.Deserialize<TribesSerializedData>(TribesData, _jsonOptions);

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
