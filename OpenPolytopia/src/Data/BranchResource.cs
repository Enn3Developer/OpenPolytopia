namespace OpenPolytopia.Data;

using System;
using Common;
using Godot;

/// <summary>
/// One branch of <c>tech_tree.json</c>, editable from the inspector
/// </summary>
[Tool]
[GlobalClass]
public partial class BranchResource : Resource {
  /// <summary>
  /// The branch these nodes belong to
  /// </summary>
  [Export]
  public BranchType Type { get; set; }

  /// <summary>
  /// The ids of the nodes of the branch, from tier 0 to tier 2 (see <see cref="SubTreeTech"/>)
  /// </summary>
  [Export]
  public string[] Nodes { get; set; } = [];

  /// <summary>
  /// Builds the resource from a branch
  /// </summary>
  /// <param name="branch">the branch</param>
  public static BranchResource FromData(BranchSerializedData branch) {
    ArgumentNullException.ThrowIfNull(branch);
    ArgumentNullException.ThrowIfNull(branch.Nodes);

    return new BranchResource { Type = branch.Type, Nodes = [.. branch.Nodes] };
  }

  /// <summary>
  /// Converts the resource back to a branch
  /// </summary>
  public BranchSerializedData ToData() => new() { Type = Type, Nodes = [.. Nodes] };
}
