namespace OpenPolytopia.Data;

using System;
using System.IO;
using Common;
using Godot;
using FileAccess = Godot.FileAccess;

/// <summary>
/// Reads and writes the json data files of <see cref="Common"/> as resources
/// </summary>
/// <remarks>
/// The file name decides the schema, the same way <see cref="EmbeddedResources"/> finds the files: <c>troops.json</c>
/// is a <see cref="TroopsResource"/>, <c>tribes.json</c> a <see cref="TribesResource"/> and <c>tech_tree.json</c> a
/// <see cref="TechTreeResource"/>; any other json file is left alone
/// </remarks>
public static class JsonData {
  /// <summary>
  /// Whether a path is one of the json data files
  /// </summary>
  /// <param name="path">the path of the file</param>
  public static bool Handles(string path) => ResourceClassOf(path) != null;

  /// <summary>
  /// The name of the resource class a json data file is read as
  /// </summary>
  /// <param name="path">the path of the file</param>
  /// <returns>the class name, or null if the file isn't a json data file</returns>
  public static string? ResourceClassOf(string path) => Path.GetFileName(path) switch {
    "troops.json" => nameof(TroopsResource),
    "tribes.json" => nameof(TribesResource),
    "tech_tree.json" => nameof(TechTreeResource),
    _ => null
  };

  /// <summary>
  /// Whether a class name is one of the resource classes the json data files are read as
  /// </summary>
  /// <param name="className">the class name</param>
  public static bool IsResourceClass(string className) =>
    className is nameof(TroopsResource) or nameof(TribesResource) or nameof(TechTreeResource);

  /// <summary>
  /// Whether a resource can be written as a json data file
  /// </summary>
  /// <param name="resource">the resource</param>
  public static bool Handles(Resource resource) => resource is TroopsResource or TribesResource or TechTreeResource;

  /// <summary>
  /// Whether a resource can be written to a path
  /// </summary>
  /// <remarks>
  /// Any json file is fine as long as it isn't the name of another data file, e.g. a <see cref="TroopsResource"/>
  /// can't be saved as <c>tribes.json</c>; mind that only the data file names are read back as resources
  /// </remarks>
  /// <param name="resource">the resource</param>
  /// <param name="path">the path of the file</param>
  public static bool Handles(Resource resource, string path) {
    if (!Handles(resource) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var resourceClass = ResourceClassOf(path);
    return resourceClass == null || resourceClass == resource.GetType().Name;
  }

  /// <summary>
  /// Parses a json document with the schema of the file it comes from
  /// </summary>
  /// <param name="path">the path of the file, only its name matters</param>
  /// <param name="json">the content of the file</param>
  /// <returns>the resource, or null if the file isn't a json data file</returns>
  /// <exception cref="System.Text.Json.JsonException">threw when the json doesn't follow the schema</exception>
  /// <exception cref="ArgumentNullException">threw when the json is empty or misses a required list</exception>
  public static Resource? Parse(string path, string json) => Path.GetFileName(path) switch {
    "troops.json" => TroopsResource.FromData(EmbeddedResources.ParseTroops(json)!),
    "tribes.json" => TribesResource.FromData(EmbeddedResources.ParseTribes(json)!),
    "tech_tree.json" => TechTreeResource.FromData(EmbeddedResources.ParseTechTree(json)!),
    _ => null
  };

  /// <summary>
  /// Serializes a resource to the json document of its schema
  /// </summary>
  /// <param name="resource">the resource</param>
  /// <returns>the json document, or null if the resource isn't a json data resource</returns>
  /// <exception cref="InvalidOperationException">threw when the resource has empty elements</exception>
  public static string? Serialize(Resource resource) => resource switch {
    TroopsResource troops => EmbeddedResources.Serialize(troops.ToData()),
    TribesResource tribes => EmbeddedResources.Serialize(tribes.ToData()),
    TechTreeResource techTree => EmbeddedResources.Serialize(techTree.ToData()),
    _ => null
  };

  /// <summary>
  /// Reads a json data file as a resource
  /// </summary>
  /// <param name="path">the path of the file</param>
  /// <returns>the resource, or null if the file isn't a json data file</returns>
  /// <exception cref="IOException">threw when the file can't be opened</exception>
  public static Resource? Load(string path) {
    if (!Handles(path)) {
      return null;
    }

    using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read) ??
                     throw new IOException($"can't open {path}: {FileAccess.GetOpenError()}");
    return Parse(path, file.GetAsText());
  }

  /// <summary>
  /// Writes a resource to a json data file
  /// </summary>
  /// <param name="resource">the resource</param>
  /// <param name="path">the path of the file, see <see cref="Handles(Resource, string)"/></param>
  /// <exception cref="ArgumentException">threw when the resource can't be written to that path</exception>
  /// <exception cref="IOException">threw when the file can't be opened</exception>
  public static void Save(Resource resource, string path) {
    if (!Handles(resource, path)) {
      throw new ArgumentException($"{resource.GetType().Name} can't be saved as {path}", nameof(path));
    }

    var json = Serialize(resource)!;
    using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write) ??
                     throw new IOException($"can't open {path}: {FileAccess.GetOpenError()}");
    file.StoreString(json + "\n");
  }
}
