namespace OpenPolytopia.Common;

using System.Reflection;

public static class EmbeddedResources {
  /// <summary>
  /// Get the troops data from the json file
  /// </summary>
  public static string TroopsData => GetResource("OpenPolytopia.Common.resources.troops.json");

  /// <summary>
  /// Get the tribes data from the json file
  /// </summary>
  public static string TribesData => GetResource("OpenPolytopia.Common.resources.tribes.json");

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
