namespace OpenPolytopia;

using System;
using System.Linq;

public class TechTree {
  public SubTreeTech ClimbingBranch { get; } = new([
    new NodeTech { Id = "climbing" },
    new NodeTech { Id = "mining" },
    new NodeTech { Id = "meditation" },
    new NodeTech { Id = "smithery" },
    new NodeTech { Id = "philosophy" }
  ]);

  public SubTreeTech FishingBranch { get; } = new([
    new NodeTech { Id = "fishing" },
    new NodeTech { Id = "sailing" },
    new NodeTech { Id = "ramming" },
    new NodeTech { Id = "navigation" },
    new NodeTech { Id = "aquatism" }
  ]);

  public SubTreeTech HuntingBranch { get; } = new([
    new NodeTech { Id = "hunting" },
    new NodeTech { Id = "archery" },
    new NodeTech { Id = "forestry" },
    new NodeTech { Id = "spiritualism" },
    new NodeTech { Id = "mathematics" }
  ]);

  public SubTreeTech RidingBranch { get; } = new([
    new NodeTech { Id = "riding" },
    new NodeTech { Id = "roads" },
    new NodeTech { Id = "free_spirit" },
    new NodeTech { Id = "trade" },
    new NodeTech { Id = "chivalry" }
  ]);

  public SubTreeTech OrganizationBranch { get; } = new([
    new NodeTech { Id = "organization" },
    new NodeTech { Id = "farming" },
    new NodeTech { Id = "strategy" },
    new NodeTech { Id = "construction" },
    new NodeTech { Id = "diplomacy" }
  ]);

  public SubTreeTech this[BranchType branch] =>
    branch switch {
      BranchType.Climbing => ClimbingBranch,
      BranchType.Fishing => FishingBranch,
      BranchType.Hunting => HuntingBranch,
      BranchType.Organization => OrganizationBranch,
      BranchType.Riding => RidingBranch,
      _ => throw new ArgumentOutOfRangeException(nameof(branch), branch, "branch is invalid")
    };

  public TechTree(StartingTech? startingTech = null) {
    if (startingTech == null) {
      return;
    }

    this[startingTech.Value.Branch].Research(startingTech.Value.Id);
  }
}

public enum BranchType {
  Climbing,
  Fishing,
  Hunting,
  Riding,
  Organization
}

public readonly struct StartingTech(BranchType branch, string id) {
  public BranchType Branch => branch;
  public string Id => id;
}

public class SubTreeTech(NodeTech[] nodes) {
  public NodeTech[] Nodes { get; } = nodes;

  public NodeTech? this[string id] => Nodes.FirstOrDefault(node => node.Id == id);

  public bool HasResearched(string id) => this[id]?.Researched ?? false;

  public uint ComputeCost(string id, uint cities) {
    for (var i = 0; i < Nodes.Length; i++) {
      if (Nodes[i].Id == id) {
        return i == 0 ? cities + 4 : i <= 2 ? (cities * 2) + 4 : (cities * 3) + 4;
      }
    }

    return 0;
  }

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
