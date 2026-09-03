namespace OpenPolytopia.Common;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

/// <summary>
/// The shape of a tech tree, shared by every player
/// </summary>
/// <remarks>
/// A definition only holds which node lives in which branch and in which slot, the researched state lives in the
/// <see cref="TechTree"/> built from it
/// <br/>
/// A definition is immutable, <see cref="Override"/> returns a new definition instead of modifying this one
/// </remarks>
public class TechTreeDefinition {
  private readonly Dictionary<BranchType, ReadOnlyCollection<string>> _branches;

  /// <summary>
  /// Returns the node ids of a branch, ordered by slot
  /// </summary>
  /// <param name="branch">the branch type</param>
  /// <exception cref="ArgumentOutOfRangeException">if <c>branch</c> is an invalid <see cref="BranchType"/></exception>
  public IReadOnlyList<string> this[BranchType branch] =>
    _branches.TryGetValue(branch, out var nodes)
      ? nodes
      : throw new ArgumentOutOfRangeException(nameof(branch), branch, "branch is invalid");

  private TechTreeDefinition(Dictionary<BranchType, string[]> branches) {
    _branches = branches.ToDictionary(branch => branch.Key, branch => Array.AsReadOnly(branch.Value));
  }

  /// <summary>
  /// Builds a definition from the deserialized data of a tech tree
  /// </summary>
  /// <param name="data">the deserialized data of the tech tree</param>
  /// <exception cref="ArgumentException">
  /// if a branch isn't a valid <see cref="BranchType"/>, is missing, is declared twice, doesn't have exactly
  /// <see cref="SubTreeTech.MAX_NODES"/> nodes or if the same node id is used more than once
  /// </exception>
  /// <exception cref="ArgumentNullException">
  /// if <c>data</c>, the list of the branches, a branch or the list of the nodes of a branch is null
  /// </exception>
  /// <example>
  /// <code>
  /// var data = JsonSerializer.Deserialize&lt;TechTreeSerializedData&gt;(EmbeddedResources.TechTreeData, options);
  /// var definition = TechTreeDefinition.FromSerializedData(data);
  /// </code>
  /// </example>
  public static TechTreeDefinition FromSerializedData(TechTreeSerializedData data) {
    ArgumentNullException.ThrowIfNull(data);

    // required only checks that the json set the property, and a json null sets it just fine
    ArgumentNullException.ThrowIfNull(data.Branches);

    var branches = new Dictionary<BranchType, string[]>(data.Branches.Count);
    foreach (var branch in data.Branches) {
      // a json null is an element of the list like any other, and nothing looks at the required members of a branch
      // that never got built
      ArgumentNullException.ThrowIfNull(branch);

      // the json converter of BranchType accepts numbers too, so a branch that doesn't exist can get this far
      if (!Enum.IsDefined(branch.Type)) {
        throw new ArgumentException($"branch {branch.Type} isn't a valid branch", nameof(data));
      }

      ArgumentNullException.ThrowIfNull(branch.Nodes);

      if (branch.Nodes.Any(string.IsNullOrWhiteSpace)) {
        throw new ArgumentException($"branch {branch.Type} has a node without an id", nameof(data));
      }

      if (branch.Nodes.Count != SubTreeTech.MAX_NODES) {
        throw new ArgumentException(
          $"branch {branch.Type} has {branch.Nodes.Count} nodes; every branch needs exactly {SubTreeTech.MAX_NODES} nodes",
          nameof(data));
      }

      if (!branches.TryAdd(branch.Type, [.. branch.Nodes])) {
        throw new ArgumentException($"branch {branch.Type} is declared more than once", nameof(data));
      }
    }

    foreach (var branch in Enum.GetValues<BranchType>()) {
      if (!branches.ContainsKey(branch)) {
        throw new ArgumentException($"branch {branch} is missing", nameof(data));
      }
    }

    var duplicate = branches.Values
      .SelectMany(nodes => nodes)
      .GroupBy(id => id)
      .FirstOrDefault(group => group.Count() > 1);
    if (duplicate != null) {
      throw new ArgumentException($"node {duplicate.Key} is declared more than once", nameof(data));
    }

    return new TechTreeDefinition(branches);
  }

  /// <summary>
  /// Returns a new definition where every replaced node is swapped with the node of the override
  /// </summary>
  /// <param name="overrides">the overrides to apply, in order</param>
  /// <returns>a new definition with the overrides applied</returns>
  /// <remarks>
  /// A node is always swapped in place so the branch keeps its <see cref="SubTreeTech.MAX_NODES"/> nodes and every
  /// node keeps its <see cref="NodeTech.Tier"/>
  /// </remarks>
  /// <exception cref="ArgumentException">
  /// if an override doesn't have a node to replace or an id, if a replaced node isn't in the tree or if the new id is
  /// already used by another node
  /// </exception>
  /// <exception cref="ArgumentNullException">if <c>overrides</c> or one of the overrides is null</exception>
  /// <example>
  /// <code>
  /// var definition = baseDefinition.Override([new TechOverride { Replaces = "fishing", Id = "free_diving" }]);
  /// </code>
  /// </example>
  public TechTreeDefinition Override(IEnumerable<TechOverride> overrides) {
    ArgumentNullException.ThrowIfNull(overrides);

    var branches = _branches.ToDictionary(branch => branch.Key, branch => branch.Value.ToArray());
    foreach (var techOverride in overrides) {
      // an override writes an id in the tree like the json does, so it needs the same check on it
      ArgumentNullException.ThrowIfNull(techOverride, nameof(overrides));

      if (string.IsNullOrWhiteSpace(techOverride.Replaces)) {
        throw new ArgumentException($"the override with id {techOverride.Id} doesn't replace anything",
          nameof(overrides));
      }

      if (string.IsNullOrWhiteSpace(techOverride.Id)) {
        throw new ArgumentException($"the override of {techOverride.Replaces} doesn't have an id", nameof(overrides));
      }

      var (branch, index) = Find(branches, techOverride.Replaces) ??
                            throw new ArgumentException($"node {techOverride.Replaces} isn't in the tree",
                              nameof(overrides));
      if (techOverride.Id != techOverride.Replaces && Find(branches, techOverride.Id) != null) {
        throw new ArgumentException($"node {techOverride.Id} is already in the tree", nameof(overrides));
      }

      branches[branch][index] = techOverride.Id;
    }

    return new TechTreeDefinition(branches);
  }

  /// <summary>
  /// Creates the tech tree of a player
  /// </summary>
  /// <param name="tribe">the tribe of the player, if null no override is applied and nothing is researched</param>
  /// <returns>a tech tree where only the starting node of the tribe is researched</returns>
  /// <remarks>
  /// The starting node is the tier 0 node of <see cref="Tribe.StartingBranch"/>, so it's resolved after the overrides
  /// of the tribe are applied
  /// </remarks>
  /// <exception cref="ArgumentException">
  /// if an override of the tribe is invalid, see <see cref="Override"/>
  /// </exception>
  /// <exception cref="ArgumentOutOfRangeException">
  /// if <see cref="Tribe.StartingBranch"/> isn't a valid <see cref="BranchType"/>
  /// </exception>
  /// <example>
  /// <code>
  /// var techTree = definition.CreateTechTree(tribeManager[TribeType.Imperius]);
  /// </code>
  /// </example>
  public TechTree CreateTechTree(Tribe? tribe = null) {
    if (tribe == null) {
      return new TechTree(this);
    }

    var definition = tribe.TechOverrides is { Count: > 0 } overrides ? Override(overrides) : this;
    var techTree = new TechTree(definition);
    techTree[tribe.StartingBranch].Research(definition[tribe.StartingBranch][0]);
    return techTree;
  }

  private static (BranchType, int)? Find(Dictionary<BranchType, string[]> branches, string id) {
    foreach (var (branch, nodes) in branches) {
      var index = Array.IndexOf(nodes, id);
      if (index != -1) {
        return (branch, index);
      }
    }

    return null;
  }
}

/// <summary>
/// Tech tree for a single player
/// </summary>
/// <remarks>
/// Every tech tree is built from a <see cref="TechTreeDefinition"/>, use
/// <see cref="TechTreeDefinition.CreateTechTree"/> to get the tree of a tribe with its overrides already applied
/// <br/>
/// Every tree owns its nodes, so researching something on a tree never touches the definition nor the tree of another
/// player
/// </remarks>
public class TechTree {
  private readonly Dictionary<BranchType, SubTreeTech> _branches;

  /// <summary>
  /// Gets the branch given its type
  /// </summary>
  /// <param name="branch">the branch type</param>
  /// <exception cref="ArgumentOutOfRangeException">if <c>branch</c> is an invalid <see cref="BranchType"/></exception>
  public SubTreeTech this[BranchType branch] =>
    _branches.TryGetValue(branch, out var subTree)
      ? subTree
      : throw new ArgumentOutOfRangeException(nameof(branch), branch, "branch is invalid");

  /// <summary>
  /// Initializes the tech tree from a definition, with nothing researched
  /// </summary>
  /// <param name="definition">the definition to build the tree from</param>
  /// <exception cref="ArgumentNullException">if <c>definition</c> is null</exception>
  public TechTree(TechTreeDefinition definition) {
    ArgumentNullException.ThrowIfNull(definition);

    _branches = Enum.GetValues<BranchType>()
      .ToDictionary(branch => branch, branch => new SubTreeTech(definition[branch]));
  }

  /// <summary>
  /// Finds a node by id, searching every branch
  /// </summary>
  /// <param name="id">the node id</param>
  /// <returns>the node; null if no branch has a node with that id</returns>
  public NodeTech? Find(string id) => _branches.Values.Select(branch => branch[id]).FirstOrDefault(node => node != null);

  /// <summary>
  /// Whether a node has been researched
  /// </summary>
  /// <param name="id">the node id</param>
  /// <returns>if the node has been researched; always false if no node has been found with that id</returns>
  public bool HasResearched(string id) => Find(id)?.Researched ?? false;

  /// <summary>
  /// Whether a node can be researched right now
  /// </summary>
  /// <param name="id">the node id</param>
  /// <returns>
  /// false if the node doesn't exist, is already researched or its parent (if it has one) isn't researched yet
  /// </returns>
  public bool CanResearch(string id) => _branches.Values.Any(branch => branch.CanResearch(id));

  /// <summary>
  /// Computes the cost of a node
  /// </summary>
  /// <param name="id">the node id</param>
  /// <param name="cities">number of cities owned by the player</param>
  /// <returns>the cost of the node; null if no branch has a node with that id</returns>
  public uint? ComputeCost(string id, uint cities) =>
    _branches.Values.Select(branch => branch.ComputeCost(id, cities)).FirstOrDefault(cost => cost != null);

  /// <summary>
  /// Marks a node as researched
  /// </summary>
  /// <param name="id">the node id</param>
  /// <returns>if the node has been marked; false if no branch has a node with that id</returns>
  public bool Research(string id) => _branches.Values.Any(branch => branch.Research(id));

  /// <summary>
  /// Returns the ids of every researched node
  /// </summary>
  /// <returns>the ids, in branch declaration order then slot order</returns>
  public IEnumerable<string> ResearchedIds() =>
    Enum.GetValues<BranchType>()
      .SelectMany(branch => this[branch].Nodes)
      .Where(node => node.Researched)
      .Select(node => node.Id);
}

/// <summary>
/// Ids of the tech nodes referenced directly by code; every other node is pure data
/// </summary>
public static class TechIds {
  /// <summary>
  /// Unlocks land troops entering mountains
  /// </summary>
  public const string CLIMBING = "climbing";
}

/// <summary>
/// Branch types
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter<BranchType>))]
public enum BranchType {
  [EnumMember(Value = "climbing")] Climbing,
  [EnumMember(Value = "fishing")] Fishing,
  [EnumMember(Value = "hunting")] Hunting,
  [EnumMember(Value = "riding")] Riding,
  [EnumMember(Value = "organization")] Organization
}

/// <summary>
/// A node of the default tech tree replaced by a tribe
/// </summary>
/// <remarks>
/// The replaced node is found by its id because every id is unique in the tree, the new node takes its slot so it also
/// takes its tier and its cost
/// </remarks>
public class TechOverride {
  /// <summary>
  /// The id of the node to replace
  /// </summary>
  public required string Replaces { get; init; }

  /// <summary>
  /// The id of the node taking its place
  /// </summary>
  public required string Id { get; init; }
}

/// <summary>
/// A subtree tech, or a branch
/// </summary>
/// <remarks>
/// The nodes of a branch are always <see cref="MAX_NODES"/>; node 0: tier 0; node 1 and node 2: tier 1;
/// node 3 and node 4: tier 2
/// <br/>
/// The tier of a node comes from its slot, so a branch builds its own nodes to make sure no node can ever get a tier
/// that doesn't belong to its slot
/// </remarks>
public class SubTreeTech {
  /// <summary>
  /// How many nodes a branch has
  /// </summary>
  public const int MAX_NODES = 5;

  private readonly NodeTech[] _nodes;

  /// <summary>
  /// The nodes of this branch, ordered by slot
  /// </summary>
  /// <remarks>
  /// It's read only because the slot of a node is what gives it its tier, so swapping a node with another one would
  /// let it take a tier that isn't the one of its slot
  /// </remarks>
  public IReadOnlyList<NodeTech> Nodes => _nodes;

  /// <summary>
  /// Returns the node that has that id, or null if no node has been found
  /// </summary>
  /// <param name="id">the node id</param>
  public NodeTech? this[string id] => Array.Find(_nodes, node => node.Id == id);

  /// <summary>
  /// Initializes a branch with its nodes, none of them researched
  /// </summary>
  /// <param name="ids">the ids of the nodes, ordered by slot</param>
  /// <exception cref="ArgumentException">
  /// if <c>ids</c> doesn't have exactly <see cref="MAX_NODES"/> ids, if an id is null or blank or if the same id is
  /// used twice
  /// </exception>
  /// <exception cref="ArgumentNullException">if <c>ids</c> is null</exception>
  /// <remarks>
  /// A node is always looked up by its id, so an id that isn't an id is a slot no one can research nor price and two
  /// nodes sharing one make the second slot the unreachable one
  /// </remarks>
  public SubTreeTech(IReadOnlyList<string> ids) {
    ArgumentNullException.ThrowIfNull(ids);

    if (ids.Count != MAX_NODES) {
      throw new ArgumentException($"a branch needs exactly {MAX_NODES} nodes, got {ids.Count}", nameof(ids));
    }

    if (ids.Any(string.IsNullOrWhiteSpace)) {
      throw new ArgumentException("a branch can't have a node without an id", nameof(ids));
    }

    if (ids.Distinct().Count() != ids.Count) {
      throw new ArgumentException("a branch can't have the same node id twice", nameof(ids));
    }

    _nodes = [.. ids.Select((id, index) => new NodeTech { Id = id, Tier = TierOf(index) })];
  }

  /// <summary>
  /// Returns the tier of a node given its slot in the branch
  /// </summary>
  /// <param name="index">the slot of the node</param>
  /// <exception cref="ArgumentOutOfRangeException">if <c>index</c> isn't a slot of the branch</exception>
  public static uint TierOf(int index) =>
    index switch {
      0 => 0,
      1 or 2 => 1,
      3 or 4 => 2,
      _ => throw new ArgumentOutOfRangeException(nameof(index), index, $"index must be between 0 and {MAX_NODES - 1}")
    };

  /// <summary>
  /// Whether a node has been marked as researched
  /// </summary>
  /// <param name="id">the node id</param>
  /// <returns>if the node has been researched; always false if no node has been found with that id</returns>
  /// <remarks>
  /// This is the shorthand for the check every caller wants, so a node that doesn't exist and a node no one researched
  /// yet both answer false; use <see cref="this[string]"/> when the two cases have to be told apart
  /// </remarks>
  public bool HasResearched(string id) => this[id]?.Researched ?? false;

  /// <summary>
  /// Computes the cost for a given node by knowing how many cities a player has
  /// </summary>
  /// <param name="id">the node id</param>
  /// <param name="cities">number of cities owned by the player</param>
  /// <returns>the cost for that node; null if no node has been found with that id</returns>
  /// <remarks>
  /// It returns null and not 0 because 0 is a price a caller can pay, so a node that doesn't exist would be free
  /// </remarks>
  public uint? ComputeCost(string id, uint cities) {
    var node = this[id];
    return node == null ? null : (cities * (node.Tier + 1)) + 4;
  }

  /// <summary>
  /// Marks a node as researched
  /// </summary>
  /// <param name="id">the node id</param>
  /// <returns>if the node has been marked; false if no node has been found with that id</returns>
  public bool Research(string id) {
    var tech = this[id];
    if (tech == null) {
      return false;
    }

    tech.Researched = true;
    return true;
  }

  /// <summary>
  /// Returns the slot of the node a node depends on
  /// </summary>
  /// <param name="index">the slot of the node</param>
  /// <returns>the slot of the parent; -1 if the node is tier 0 and has no parent</returns>
  /// <remarks>
  /// Slot 1 and slot 2 both need slot 0 (tier 0); slot 3 needs slot 1; slot 4 needs slot 2
  /// </remarks>
  /// <exception cref="ArgumentOutOfRangeException">if <c>index</c> isn't a slot of the branch</exception>
  public static int ParentOf(int index) =>
    index switch {
      0 => -1,
      1 or 2 => 0,
      3 => 1,
      4 => 2,
      _ => throw new ArgumentOutOfRangeException(nameof(index), index, $"index must be between 0 and {MAX_NODES - 1}")
    };

  /// <summary>
  /// Returns the slot of a node
  /// </summary>
  /// <param name="id">the node id</param>
  /// <returns>the slot; -1 if no node has been found with that id</returns>
  public int IndexOf(string id) => Array.FindIndex(_nodes, node => node.Id == id);

  /// <summary>
  /// Whether a node can be researched right now
  /// </summary>
  /// <param name="id">the node id</param>
  /// <returns>
  /// false if the node doesn't exist, is already researched or its parent (if it has one) isn't researched yet
  /// </returns>
  public bool CanResearch(string id) {
    var index = IndexOf(id);
    if (index == -1 || _nodes[index].Researched) {
      return false;
    }

    var parent = ParentOf(index);
    return parent == -1 || _nodes[parent].Researched;
  }
}

/// <summary>
/// A node of a branch
/// </summary>
public class NodeTech {
  /// <summary>
  /// Whether the player has researched this node
  /// </summary>
  /// <remarks>
  /// The researched state is set only inside this assembly and <see cref="SubTreeTech.Research"/> is the only place
  /// doing it, so researching something has a single place where it can be paid for
  /// </remarks>
  public bool Researched { get; internal set; }

  /// <summary>
  /// The id of this node
  /// </summary>
  public required string Id { get; init; }

  /// <summary>
  /// The tier of this node, it's what makes its cost grow
  /// </summary>
  /// <remarks>
  /// The tier is the one of the slot of the node, so only this assembly builds nodes and
  /// <see cref="SubTreeTech"/> is the only type doing it
  /// </remarks>
  public uint Tier { get; internal init; }
}

public class BranchSerializedData {
  public required BranchType Type { get; init; }
  public required List<string> Nodes { get; init; }
}

public class TechTreeSerializedData {
  public required List<BranchSerializedData> Branches { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TechOverride))]
[JsonSerializable(typeof(BranchSerializedData))]
[JsonSerializable(typeof(TechTreeSerializedData))]
public partial class TechTreeGenerationContext : JsonSerializerContext;
