namespace OpenPolytopia;

using System;
using System.Text.Json;
using System.Text.Unicode;
using Common;
using Godot;
using Shouldly;

public class TechTreeTest {
  private static readonly JsonSerializerOptions _techTreeOptions = new() {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All),
    TypeInfoResolver = TechTreeGenerationContext.Default,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
  };

  private static readonly JsonSerializerOptions _tribeOptions = new() {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All),
    TypeInfoResolver = TribeGenerationContext.Default,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
  };

  private static readonly SpawnRate _spawnRate = new() {
    FruitRate = 1f, CropRate = 1f, AnimalRate = 1f, FishRate = 1f, MineralRate = 1f
  };

  private static readonly TerrainRate _terrainRate = new() {
    ForestRate = 1f, MountainRate = 1f, WaterRate = 1f
  };

  private readonly TechTreeDefinition _definition = LoadDefinition();

  private static TechTreeDefinition LoadDefinition() {
    var data = JsonSerializer.Deserialize<TechTreeSerializedData>(EmbeddedResources.TechTreeData, _techTreeOptions);
    data.ShouldNotBeNull();
    return TechTreeDefinition.FromSerializedData(data);
  }

  [Fact]
  public void TestDefaultTree() {
    _definition[BranchType.Climbing].Count.ShouldBe(SubTreeTech.MAX_NODES);
    _definition[BranchType.Climbing][0].ShouldBe("climbing");
    _definition[BranchType.Organization][4].ShouldBe("diplomacy");
  }

  [Fact]
  public void TestNothingResearched() {
    var techTree = _definition.CreateTechTree();
    techTree[BranchType.Climbing].HasResearched("climbing").ShouldBeFalse();
  }

  [Fact]
  public void TestResearch() {
    var techTree = _definition.CreateTechTree();
    techTree[BranchType.Climbing].Research("climbing").ShouldBeTrue();
    techTree[BranchType.Climbing].HasResearched("climbing").ShouldBeTrue();
  }

  [Fact]
  public void TestResearchUnknownNode() {
    var techTree = _definition.CreateTechTree();
    techTree[BranchType.Climbing].Research("swimming").ShouldBeFalse();
    techTree[BranchType.Climbing].HasResearched("swimming").ShouldBeFalse();
  }

  [Fact]
  public void TestInvalidBranch() =>
    Should.Throw<ArgumentOutOfRangeException>(() => _definition.CreateTechTree()[(BranchType)42]);

  [Fact]
  public void TestComputeCost() {
    var branch = _definition.CreateTechTree()[BranchType.Climbing];
    branch.ComputeCost("climbing", 1).ShouldBe(5u);
    branch.ComputeCost("climbing", 2).ShouldBe(6u);
    branch.ComputeCost("mining", 2).ShouldBe(8u);
    branch.ComputeCost("smithery", 2).ShouldBe(10u);
    branch.ComputeCost("swimming", 2).ShouldBe(0u);
  }

  [Fact]
  public void TestTiers() {
    SubTreeTech.TierOf(0).ShouldBe(0u);
    SubTreeTech.TierOf(1).ShouldBe(1u);
    SubTreeTech.TierOf(2).ShouldBe(1u);
    SubTreeTech.TierOf(3).ShouldBe(2u);
    SubTreeTech.TierOf(4).ShouldBe(2u);
    Should.Throw<ArgumentOutOfRangeException>(() => SubTreeTech.TierOf(SubTreeTech.MAX_NODES));
  }

  [Fact]
  public void TestBranchNodesCount() =>
    Should.Throw<ArgumentException>(() => new SubTreeTech([new NodeTech { Id = "climbing", Tier = 0 }]));

  [Fact]
  public void TestMissingBranch() {
    var data = JsonSerializer.Deserialize<TechTreeSerializedData>(EmbeddedResources.TechTreeData, _techTreeOptions);
    data.ShouldNotBeNull();
    data.Branches.RemoveAt(0);
    Should.Throw<ArgumentException>(() => TechTreeDefinition.FromSerializedData(data));
  }

  [Fact]
  public void TestDuplicatedNode() {
    var data = JsonSerializer.Deserialize<TechTreeSerializedData>(EmbeddedResources.TechTreeData, _techTreeOptions);
    data.ShouldNotBeNull();
    data.Branches[1].Nodes[0] = data.Branches[0].Nodes[0];
    Should.Throw<ArgumentException>(() => TechTreeDefinition.FromSerializedData(data));
  }

  [Fact]
  public void TestOverride() {
    var definition = _definition.Override([new TechOverride { Replaces = "fishing", Id = "free_diving" }]);
    definition[BranchType.Fishing].Count.ShouldBe(SubTreeTech.MAX_NODES);
    definition[BranchType.Fishing][0].ShouldBe("free_diving");
    definition[BranchType.Fishing][1].ShouldBe("sailing");
    definition.CreateTechTree()[BranchType.Fishing].ComputeCost("free_diving", 2).ShouldBe(6u);
  }

  [Fact]
  public void TestOverrideKeepsTheDefinition() {
    _definition.Override([new TechOverride { Replaces = "fishing", Id = "free_diving" }]);
    _definition[BranchType.Fishing][0].ShouldBe("fishing");
  }

  [Fact]
  public void TestOverrideUnknownNode() =>
    Should.Throw<ArgumentException>(() =>
      _definition.Override([new TechOverride { Replaces = "swimming", Id = "free_diving" }]));

  [Fact]
  public void TestOverrideDuplicatedNode() =>
    Should.Throw<ArgumentException>(() =>
      _definition.Override([new TechOverride { Replaces = "fishing", Id = "mining" }]));

  [Fact]
  public void TestChainedOverrides() {
    var definition = _definition.Override([
      new TechOverride { Replaces = "fishing", Id = "free_diving" },
      new TechOverride { Replaces = "free_diving", Id = "deep_diving" }
    ]);
    definition[BranchType.Fishing][0].ShouldBe("deep_diving");
  }

  [Fact]
  public void TestStartingBranch() {
    var tribe = new Tribe {
      StartingBranch = BranchType.Climbing,
      StartingStars = 5,
      SpawnRate = _spawnRate,
      TerrainRate = _terrainRate
    };
    var techTree = _definition.CreateTechTree(tribe);
    techTree[BranchType.Climbing].HasResearched("climbing").ShouldBeTrue();
    techTree[BranchType.Climbing].HasResearched("mining").ShouldBeFalse();
  }

  [Fact]
  public void TestStartingBranchWithOverride() {
    var tribe = new Tribe {
      StartingBranch = BranchType.Fishing,
      StartingStars = 5,
      SpawnRate = _spawnRate,
      TerrainRate = _terrainRate,
      TechOverrides = [new TechOverride { Replaces = "fishing", Id = "free_diving" }]
    };
    var techTree = _definition.CreateTechTree(tribe);
    techTree[BranchType.Fishing].HasResearched("free_diving").ShouldBeTrue();
    techTree[BranchType.Fishing].HasResearched("fishing").ShouldBeFalse();
  }

  [Fact]
  public void TestTribesResource() {
    var tribes = JsonSerializer.Deserialize<TribesSerializedData>(EmbeddedResources.TribesData, _tribeOptions);
    tribes.ShouldNotBeNull();
    var tribeManager = new TribeManager();
    tribeManager.RegisterTribes(tribes);
    var imperius = tribeManager[TribeType.Imperius];
    imperius.ShouldNotBeNull();
    imperius.StartingBranch.ShouldBe(BranchType.Organization);
    _definition.CreateTechTree(imperius)[BranchType.Organization].HasResearched("organization").ShouldBeTrue();
  }
}
