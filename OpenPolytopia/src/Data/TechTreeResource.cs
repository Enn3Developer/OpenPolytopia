namespace OpenPolytopia.Data;

using System;
using System.Linq;
using Common;
using Godot;
using Godot.Collections;

/// <summary>
/// The content of <c>tech_tree.json</c>, editable from the inspector
/// </summary>
[Tool]
[GlobalClass]
public partial class TechTreeResource : Resource {
  /// <summary>
  /// The branches of the default tech tree
  /// </summary>
  [Export]
  public Array<BranchResource> Branches { get; set; } = new();

  /// <summary>
  /// Builds the resource from the deserialized json
  /// </summary>
  /// <param name="data">the tech tree data</param>
  public static TechTreeResource FromData(TechTreeSerializedData data) {
    ArgumentNullException.ThrowIfNull(data);
    ArgumentNullException.ThrowIfNull(data.Branches);

    return new TechTreeResource { Branches = [.. data.Branches.Select(BranchResource.FromData)] };
  }

  /// <summary>
  /// Converts the resource back to the json data
  /// </summary>
  /// <exception cref="InvalidOperationException">threw when an element of <see cref="Branches"/> is empty</exception>
  public TechTreeSerializedData ToData() => new() {
    Branches = Branches.Select((branch, i) =>
      (branch ?? throw new InvalidOperationException($"branch {i} is empty")).ToData()).ToList()
  };
}
