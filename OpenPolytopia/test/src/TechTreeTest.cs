namespace OpenPolytopia;

using Chickensoft.GoDotTest;
using Common;
using Godot;
using Shouldly;

public class TechTreeTest(Node testScene) : TestClass(testScene) {
  [Test]
  public void TestStartingTech() {
    const BranchType branch = BranchType.Climbing;
    const string id = "climbing";
    var startingTech = new StartingTech { Branch = branch, Id = id };
    var techTree = new TechTree(startingTech);
    techTree[branch].HasResearched(id).ShouldBeTrue();
  }

  [Test]
  public void TestStartingTech2() {
    const BranchType branch = BranchType.Climbing;
    const string id = "climbing";
    var techTree = new TechTree();
    techTree[branch].HasResearched(id).ShouldBeFalse();
  }

  [Test]
  public void TestResearch() {
    const BranchType branch = BranchType.Climbing;
    const string id = "climbing";
    var techTree = new TechTree();
    techTree[branch].HasResearched(id).ShouldBeFalse();
    techTree[branch].Research(id);
    techTree[branch].HasResearched(id).ShouldBeTrue();
  }

  [Test]
  public void TestComputeCost() {
    const BranchType branch = BranchType.Climbing;
    var id = "climbing";
    var cities = 1u;
    var techTree = new TechTree();
    techTree[branch].ComputeCost(id, cities).ShouldBe(5u);
    cities = 2;
    techTree[branch].ComputeCost(id, cities).ShouldBe(6u);
    id = "smithery";
    techTree[branch].ComputeCost(id, cities).ShouldBe(10u);
  }
}
