#if TOOLS
namespace OpenPolytopia.Data.Editor;

using System;
using Godot;

/// <summary>
/// Loads the json data files as resources, see <see cref="JsonData"/>
/// </summary>
[Tool]
public partial class JsonDataLoader : ResourceFormatLoader {
  public override string[] _GetRecognizedExtensions() => ["json"];

  public override bool _HandlesType(StringName type) => type == nameof(Resource) || JsonData.IsResourceClass(type);

  public override bool _RecognizePath(string path, StringName type) =>
    JsonData.Handles(path) && (type.IsEmpty || _HandlesType(type));

  public override string _GetResourceType(string path) => JsonData.Handles(path) ? nameof(Resource) : "";

  public override string _GetResourceScriptClass(string path) => JsonData.ResourceClassOf(path) ?? "";

  // no uid so the editor doesn't litter the folder with .uid files
  public override long _GetResourceUid(string path) => ResourceUid.InvalidId;

  public override Variant _Load(string path, string originalPath, bool useSubThreads, int cacheMode) {
    try {
      var resource = JsonData.Load(path);
      if (resource == null) {
        return (int)Error.FileUnrecognized;
      }

      return resource;
    }
    catch (Exception e) {
      GD.PushError($"can't load {path}: {e.Message}");
      return (int)Error.ParseError;
    }
  }
}
#endif
