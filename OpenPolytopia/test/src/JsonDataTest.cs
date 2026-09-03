namespace OpenPolytopia;

using System;
using System.Linq;
using Chickensoft.GoDotTest;
using Common;
using Data;
using Godot;
using Shouldly;
using FileAccess = Godot.FileAccess;
#if TOOLS
using Data.Editor;
#endif

public class JsonDataTest(Node testScene) : TestClass(testScene) {
  private const string TROOPS = "res://resources/troops.json";
  private const string TRIBES = "res://resources/tribes.json";
  private const string TECH_TREE = "res://resources/tech_tree.json";

  [Test]
  public void TestHandlesOnlyTheDataFiles() {
    JsonData.Handles(TROOPS).ShouldBeTrue();
    JsonData.Handles(TRIBES).ShouldBeTrue();
    JsonData.Handles(TECH_TREE).ShouldBeTrue();
    JsonData.Handles("res://resources/other.json").ShouldBeFalse();
    JsonData.Handles("res://project.godot").ShouldBeFalse();
    JsonData.ResourceClassOf(TROOPS).ShouldBe(nameof(TroopsResource));
    JsonData.ResourceClassOf(TRIBES).ShouldBe(nameof(TribesResource));
    JsonData.ResourceClassOf(TECH_TREE).ShouldBe(nameof(TechTreeResource));
  }

  [Test]
  public void TestLoadTroops() {
    var resource = JsonData.Load(TROOPS).ShouldBeOfType<TroopsResource>();
    var data = EmbeddedResources.LoadTroops().ShouldNotBeNull();

    resource.Troops.Count.ShouldBe(data.Troops.Count);
    var warrior = resource.Troops.Single(troop => troop.Type == TroopType.Warrior);
    warrior.MaxHp.ShouldBe(10u);
    warrior.Attack.ShouldBe(2u);
    warrior.Skills.ShouldBe([(int)Skill.Dash, (int)Skill.Fortify]);
  }

  [Test]
  public void TestLoadTribes() {
    var resource = JsonData.Load(TRIBES).ShouldBeOfType<TribesResource>();
    var imperius = resource.Tribes.Single(tribe => tribe.TribeType == TribeType.Imperius);

    imperius.StartingBranch.ShouldBe(BranchType.Organization);
    imperius.StartingStars.ShouldBe(7);
    imperius.FruitRate.ShouldBe(2.0f);
    imperius.AnimalRate.ShouldBe(0.5f);
    imperius.TechOverrides.ShouldBeEmpty();
  }

  [Test]
  public void TestLoadTechTree() {
    var resource = JsonData.Load(TECH_TREE).ShouldBeOfType<TechTreeResource>();
    var climbing = resource.Branches.Single(branch => branch.Type == BranchType.Climbing);

    climbing.Nodes.Length.ShouldBe(SubTreeTech.MAX_NODES);
    climbing.Nodes[0].ShouldBe("climbing");
  }

  [Test]
  public void TestLoadOtherJson() => JsonData.Load("res://resources/other.json").ShouldBeNull();

  [Test]
  public void TestRoundTrip() {
    // the same data serialized from the resource and from the deserialized json must match
    JsonData.Serialize(JsonData.Load(TROOPS)!)
      .ShouldBe(EmbeddedResources.Serialize(EmbeddedResources.LoadTroops()!));
    JsonData.Serialize(JsonData.Load(TRIBES)!)
      .ShouldBe(EmbeddedResources.Serialize(EmbeddedResources.LoadTribes()!));
    JsonData.Serialize(JsonData.Load(TECH_TREE)!)
      .ShouldBe(EmbeddedResources.Serialize(EmbeddedResources.LoadTechTree()!));
  }

  [Test]
  public void TestSerializedFollowsTheSchema() {
    var json = JsonData.Serialize(JsonData.Load(TROOPS)!)!;
    var troops = EmbeddedResources.ParseTroops(json).ShouldNotBeNull();
    troops.Troops.Count.ShouldBe(EmbeddedResources.LoadTroops()!.Troops.Count);
    // enums keep the names used in the files
    json.ShouldContain("\"mindBender\"", Case.Sensitive);
    json.ShouldContain("\"dash\"", Case.Sensitive);
    json.ShouldNotContain("\"Dash\"", Case.Sensitive);

    var tribes = JsonData.Serialize(JsonData.Load(TRIBES)!)!;
    tribes.ShouldContain("\"imperius\"", Case.Sensitive);
    tribes.ShouldContain("\"spawn_rate\"");
    tribes.ShouldContain("\"terrain_rate\"");
    tribes.ShouldNotContain("tech_overrides");
  }

  [Test]
  public void TestTechOverridesAreWritten() {
    var tribe = new TribeResource {
      TribeType = TribeType.Bardur,
      TechOverrides = [new TechOverrideResource { Replaces = "climbing", Id = "ice_fishing" }]
    };
    var data = tribe.ToData();
    data.Tribe.TechOverrides.ShouldNotBeNull().Single().Id.ShouldBe("ice_fishing");

    var json = EmbeddedResources.Serialize(new TribesSerializedData { Tribes = [data] });
    json.ShouldContain("\"tech_overrides\"");
    var back = TribesResource.FromData(EmbeddedResources.ParseTribes(json)!);
    back.Tribes.Single().TechOverrides.Single().Replaces.ShouldBe("climbing");
  }

  [Test]
  public void TestEmptyElementsAreRejected() {
    Should.Throw<InvalidOperationException>(() => new TroopsResource { Troops = [null!] }.ToData());
    Should.Throw<InvalidOperationException>(() => new TribesResource { Tribes = [null!] }.ToData());
    Should.Throw<InvalidOperationException>(() => new TechTreeResource { Branches = [null!] }.ToData());
    Should.Throw<InvalidOperationException>(() =>
      new TribeResource { TechOverrides = [null!] }.ToData());
    Should.Throw<InvalidOperationException>(() => new TroopResource { Skills = [999] }.ToData());
  }

  [Test]
  public void TestSkillsAreEnumsInTheInspector() {
    var property = new TroopResource().GetPropertyList()
      .Single(property => property["name"].AsString() == nameof(TroopResource.Skills));

    property["hint"].AsInt32().ShouldBe((int)PropertyHint.TypeString);
    var hint = property["hint_string"].AsString();
    hint.ShouldStartWith($"{(int)Variant.Type.Int}/{(int)PropertyHint.Enum}:");
    hint.ShouldContain(nameof(Skill.Dash));
  }

  [Test]
  public void TestSave() {
    var path = "user://troops.json";
    var resource = (TroopsResource)JsonData.Load(TROOPS)!;
    resource.Troops[0].MaxHp = 42;

    JsonData.Save(resource, path);
    try {
      using (var file = FileAccess.Open(path, FileAccess.ModeFlags.Read)) {
        file.GetAsText().ShouldEndWith("\n");
      }

      var back = (TroopsResource)JsonData.Load(path)!;
      back.Troops[0].MaxHp.ShouldBe(42u);
      JsonData.Serialize(back).ShouldBe(JsonData.Serialize(resource));
    }
    finally {
      DirAccess.RemoveAbsolute(path);
    }
  }

  [Test]
  public void TestSaveWithTheWrongName() {
    Should.Throw<ArgumentException>(() => JsonData.Save(new TroopsResource(), "user://tribes.json"));
    Should.Throw<ArgumentException>(() => JsonData.Save(new TroopsResource(), "user://troops.tres"));
    Should.Throw<ArgumentException>(() => JsonData.Save(new Resource(), "user://troops.json"));
  }

  // the loader and the saver only exist in editor builds
#if TOOLS
  [Test]
  public void TestLoader() {
    var loader = new JsonDataLoader();

    loader._GetRecognizedExtensions().ShouldBe(["json"]);
    loader._RecognizePath(TROOPS, "").ShouldBeTrue();
    loader._RecognizePath(TROOPS, nameof(Resource)).ShouldBeTrue();
    loader._RecognizePath(TROOPS, nameof(PackedScene)).ShouldBeFalse();
    loader._RecognizePath("res://resources/other.json", "").ShouldBeFalse();
    loader._GetResourceType(TROOPS).ShouldBe(nameof(Resource));
    loader._GetResourceType("res://resources/other.json").ShouldBe("");
    loader._GetResourceScriptClass(TRIBES).ShouldBe(nameof(TribesResource));
    loader._GetResourceUid(TROOPS).ShouldBe(ResourceUid.InvalidId);

    loader._Load(TECH_TREE, TECH_TREE, false, 0).As<GodotObject>().ShouldBeOfType<TechTreeResource>();
    loader._Load("res://resources/missing.json", "", false, 0).AsInt32().ShouldBe((int)Error.FileUnrecognized);
  }

  [Test]
  public void TestSaver() {
    var saver = new JsonDataSaver();
    var resource = (TribesResource)JsonData.Load(TRIBES)!;

    saver._Recognize(resource).ShouldBeTrue();
    saver._Recognize(new Resource()).ShouldBeFalse();
    saver._GetRecognizedExtensions(resource).ShouldBe(["json"]);
    saver._GetRecognizedExtensions(new Resource()).ShouldBeEmpty();
    saver._RecognizePath(resource, "user://tribes.json").ShouldBeTrue();
    saver._RecognizePath(resource, "user://backup.json").ShouldBeTrue();
    saver._RecognizePath(resource, "user://troops.json").ShouldBeFalse();
    saver._RecognizePath(resource, "user://tribes.tres").ShouldBeFalse();

    var path = "user://tribes.json";
    saver._Save(resource, path, 0).ShouldBe(Error.Ok);
    try {
      JsonData.Load(path).ShouldBeOfType<TribesResource>();
    }
    finally {
      DirAccess.RemoveAbsolute(path);
    }

    // saving an empty element fails instead of writing a broken file
    saver._Save(new TribesResource { Tribes = [null!] }, path, 0).ShouldBe(Error.CantCreate);
    FileAccess.FileExists(path).ShouldBeFalse();
  }
#endif
}
