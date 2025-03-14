namespace OpenPolytopia.Common;

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

/// <summary>
/// Tech tree for a single player
/// </summary>
public class TechTree {
  /// <summary>
  /// Climbing branch
  /// </summary>
  public SubTreeTech ClimbingBranch { get; } = new([
    new NodeTech { Id = "climbing" },
    new NodeTech { Id = "mining" },
    new NodeTech { Id = "meditation" },
    new NodeTech { Id = "smithery" },
    new NodeTech { Id = "philosophy" }
  ]);

  /// <summary>
  /// Fishing branch
  /// </summary>
  public SubTreeTech FishingBranch { get; } = new([
    new NodeTech { Id = "fishing" },
    new NodeTech { Id = "sailing" },
    new NodeTech { Id = "ramming" },
    new NodeTech { Id = "navigation" },
    new NodeTech { Id = "aquatism" }
  ]);

  /// <summary>
  /// Hunting branch
  /// </summary>
  public SubTreeTech HuntingBranch { get; } = new([
    new NodeTech { Id = "hunting" },
    new NodeTech { Id = "archery" },
    new NodeTech { Id = "forestry" },
    new NodeTech { Id = "spiritualism" },
    new NodeTech { Id = "mathematics" }
  ]);

  /// <summary>
  /// Riding branch
  /// </summary>
  public SubTreeTech RidingBranch { get; } = new([
    new NodeTech { Id = "riding" },
    new NodeTech { Id = "roads" },
    new NodeTech { Id = "free_spirit" },
    new NodeTech { Id = "trade" },
    new NodeTech { Id = "chivalry" }
  ]);

  /// <summary>
  /// Organization branch
  /// </summary>
  public SubTreeTech OrganizationBranch { get; } = new([
    new NodeTech { Id = "organization" },
    new NodeTech { Id = "farming" },
    new NodeTech { Id = "strategy" },
    new NodeTech { Id = "construction" },
    new NodeTech { Id = "diplomacy" }
  ]);

  /// <summary>
  /// Gets the branch given its type
  /// </summary>
  /// <param name="branch">the branch type</param>
  /// <exception cref="ArgumentOutOfRangeException">if <c>branch</c> is an invalid <see cref="BranchType"/></exception>
  public SubTreeTech this[BranchType branch] =>
    branch switch {
      BranchType.Climbing => ClimbingBranch,
      BranchType.Fishing => FishingBranch,
      BranchType.Hunting => HuntingBranch,
      BranchType.Organization => OrganizationBranch,
      BranchType.Riding => RidingBranch,
      _ => throw new ArgumentOutOfRangeException(nameof(branch), branch, "branch is invalid")
    };

  /// <summary>
  /// Initializes the tech tree with an optional starting tech
  /// </summary>
  /// <param name="startingTech">the starting tech</param>
  public TechTree(StartingTech? startingTech = null) {
    if (startingTech == null) {
      return;
    }

    this[startingTech.Value.Branch].Research(startingTech.Value.Id);
  }
}

/// <summary>
/// Branch types
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BranchType>))]
public enum BranchType {
  [EnumMember(Value = "climbing")] Climbing,
  [EnumMember(Value = "fishing")] Fishing,
  [EnumMember(Value = "hunting")] Hunting,
  [EnumMember(Value = "riding")] Riding,
  [EnumMember(Value = "organization")] Organization
}

/// <summary>
/// The starting tech struct used in <see cref="TechTree"/>
/// </summary>
public readonly struct StartingTech {
  public required BranchType Branch { get; init; }
  public required string Id { get; init; }
}

/// <summary>
/// A subtree tech, or a branch
/// </summary>
/// <param name="nodes">array of nodes in the subtree</param>
/// <remarks>
/// The max nodes of a branch is 5; node 0: tier 0; node 1 and node 2: tier 1; node 3 and node 4: tier 2
/// </remarks>
public class SubTreeTech(NodeTech[] nodes) {
  public NodeTech[] Nodes { get; } = nodes;

  /// <summary>
  /// Returns the node that has that id, or null if no node has been found
  /// </summary>
  /// <param name="id">the node id</param>
  public NodeTech? this[string id] => Nodes.FirstOrDefault(node => node.Id == id);

  /// <summary>
  /// Whether a node as been marked as researched
  /// </summary>
  /// <param name="id">the node id</param>
  /// <returns>if the node has been researched; always false if no node has been found with that id</returns>
  public bool HasResearched(string id) => this[id]?.Researched ?? false;

  /// <summary>
  /// Computes the cost for a given node by knowing how many cities a player has
  /// </summary>
  /// <param name="id">the node id</param>
  /// <param name="cities">number of cities owned by the player</param>
  /// <returns>the cost for that node</returns>
  public uint ComputeCost(string id, uint cities) {
    for (var i = 0; i < Nodes.Length; i++) {
      if (Nodes[i].Id == id) {
        return i == 0 ? cities + 4 : i <= 2 ? (cities * 2) + 4 : (cities * 3) + 4;
      }
    }

    return 0;
  }

  /// <summary>
  /// Marks a node as researched
  /// </summary>
  /// <param name="id">the node id</param>
  public void Research(string id) {
    var tech = this[id];
    if (tech != null) {
      tech.Researched = true;
    }
  }
}

public class NodeTech {
  public bool Researched { get; set; }
  public required string Id { get; init; }
}
