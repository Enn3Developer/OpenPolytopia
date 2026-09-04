namespace OpenPolytopia.Data;

using System;
using System.Linq;
using Common;
using Godot;
using Godot.Collections;

/// <summary>
/// One entry of <c>troops.json</c>, editable from the inspector
/// </summary>
[Tool]
[GlobalClass]
public partial class TroopResource : Resource {
  /// <summary>
  /// The troop this entry defines
  /// </summary>
  [Export]
  public TroopType Type { get; set; }

  /// <summary>
  /// Max hp of the troop
  /// </summary>
  [Export(PropertyHint.Range, "0,100,1,or_greater")]
  public uint MaxHp { get; set; } = 10;

  /// <summary>
  /// Attack of the troop
  /// </summary>
  [Export(PropertyHint.Range, "0,100,1,or_greater")]
  public uint Attack { get; set; }

  /// <summary>
  /// Defense of the troop
  /// </summary>
  [Export(PropertyHint.Range, "0,100,1,or_greater")]
  public uint Defense { get; set; }

  /// <summary>
  /// Movement of the troop in tiles
  /// </summary>
  [Export(PropertyHint.Range, "0,100,1,or_greater")]
  public uint Movement { get; set; } = 1;

  /// <summary>
  /// Attack range of the troop in tiles
  /// </summary>
  [Export(PropertyHint.Range, "0,100,1,or_greater")]
  public uint Range { get; set; } = 1;

  /// <summary>
  /// The <see cref="Skill"/>s of the troop
  /// </summary>
  /// <remarks>
  /// Godot can't export an array of enums, so this is an array of the enum values; the inspector still shows a
  /// dropdown for every element thanks to <see cref="_ValidateProperty"/>
  /// </remarks>
  [Export]
  public int[] Skills { get; set; } = [];

  /// <summary>
  /// Stars needed to train the troop; 0 means the troop can't be trained in a city
  /// </summary>
  [Export(PropertyHint.Range, "0,100,1,or_greater")]
  public uint Cost { get; set; }

  /// <summary>
  /// Id of the tech that unlocks the troop; empty when no tech is needed
  /// </summary>
  [Export]
  public string RequiredTech { get; set; } = "";

  public override void _ValidateProperty(Dictionary property) {
    if (property["name"].AsStringName() != PropertyName.Skills) {
      return;
    }

    // "element type/element hint:hint string", so every element is an enum dropdown
    property["hint"] = (int)PropertyHint.TypeString;
    property["hint_string"] =
      $"{(int)Variant.Type.Int}/{(int)PropertyHint.Enum}:{string.Join(",", Enum.GetNames<Skill>())}";
  }

  /// <summary>
  /// Builds the resource from a troop entry
  /// </summary>
  /// <param name="type">the troop type</param>
  /// <param name="troop">the troop</param>
  public static TroopResource FromData(TroopType type, Troop troop) {
    ArgumentNullException.ThrowIfNull(troop);

    return new TroopResource {
      Type = type,
      MaxHp = troop.MaxHp,
      Attack = troop.Attack,
      Defense = troop.Defense,
      Movement = troop.Movement,
      Range = troop.Range,
      Skills = [.. troop.Skills.Select(skill => (int)skill)],
      Cost = troop.Cost,
      RequiredTech = troop.RequiredTech ?? ""
    };
  }

  /// <summary>
  /// Converts the resource back to a troop entry
  /// </summary>
  /// <exception cref="InvalidOperationException">threw when a skill isn't a <see cref="Skill"/> value</exception>
  public TroopSerializedData ToData() {
    var skills = new Skill[Skills.Length];
    for (var i = 0; i < Skills.Length; i++) {
      if (!Enum.IsDefined((Skill)Skills[i])) {
        throw new InvalidOperationException($"skill {i} of {Type} is not a valid skill: {Skills[i]}");
      }

      skills[i] = (Skill)Skills[i];
    }

    return new TroopSerializedData {
      Type = Type,
      Troop = new Troop {
        MaxHp = MaxHp,
        Attack = Attack,
        Defense = Defense,
        Movement = Movement,
        Range = Range,
        Skills = skills,
        Cost = Cost,
        // an empty string is what the inspector shows for "no tech", the json leaves the property out instead
        RequiredTech = string.IsNullOrWhiteSpace(RequiredTech) ? null : RequiredTech
      }
    };
  }
}
