#if TOOLS
namespace OpenPolytopia.Data.Editor;

using System;
using Godot;

/// <summary>
/// Saves the json data resources back to their files, see <see cref="JsonData"/>
/// </summary>
[Tool]
public partial class JsonDataSaver : ResourceFormatSaver {
  public override string[] _GetRecognizedExtensions(Resource resource) => JsonData.Handles(resource) ? ["json"] : [];

  public override bool _Recognize(Resource resource) => JsonData.Handles(resource);

  public override bool _RecognizePath(Resource resource, string path) => JsonData.Handles(resource, path);

  public override Error _Save(Resource resource, string path, uint flags) {
    try {
      JsonData.Save(resource, path);
      return Error.Ok;
    }
    catch (Exception e) {
      GD.PushError($"can't save {path}: {e.Message}");
      return Error.CantCreate;
    }
  }
}
#endif
