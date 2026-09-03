namespace OpenPolytopia.Data;

using System;
using Common;
using Godot;

/// <summary>
/// A node of the default tech tree replaced by a tribe, editable from the inspector
/// </summary>
[Tool]
[GlobalClass]
public partial class TechOverrideResource : Resource {
  /// <summary>
  /// The id of the node to replace
  /// </summary>
  [Export]
  public string Replaces { get; set; } = "";

  /// <summary>
  /// The id of the node taking its place
  /// </summary>
  [Export]
  public string Id { get; set; } = "";

  /// <summary>
  /// Builds the resource from a tech override
  /// </summary>
  /// <param name="techOverride">the tech override</param>
  public static TechOverrideResource FromData(TechOverride techOverride) {
    ArgumentNullException.ThrowIfNull(techOverride);

    return new TechOverrideResource { Replaces = techOverride.Replaces, Id = techOverride.Id };
  }

  /// <summary>
  /// Converts the resource back to a tech override
  /// </summary>
  public TechOverride ToData() => new() { Replaces = Replaces, Id = Id };
}
