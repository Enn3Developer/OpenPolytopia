namespace OpenPolytopia.Data;

using System;
using System.Linq;
using Common;
using Godot;
using Godot.Collections;

/// <summary>
/// One entry of <c>tribes.json</c>, editable from the inspector
/// </summary>
/// <remarks>
/// The spawn and terrain rates are flattened into groups so they can be edited in place, the json keeps its nested
/// objects
/// </remarks>
[Tool]
[GlobalClass]
public partial class TribeResource : Resource {
  /// <summary>
  /// The tribe this entry defines
  /// </summary>
  [Export]
  public TribeType TribeType { get; set; }

  /// <summary>
  /// The branch the tribe starts with
  /// </summary>
  [Export]
  public BranchType StartingBranch { get; set; }

  /// <summary>
  /// The nodes of the default tech tree this tribe replaces with its own, if any
  /// </summary>
  [Export]
  public Array<TechOverrideResource> TechOverrides { get; set; } = new();

  /// <summary>
  /// The starting stars of the tribe
  /// </summary>
  [Export(PropertyHint.Range, "0,100,1,or_greater")]
  public int StartingStars { get; set; }

  /// <summary>
  /// Multiplier of the fruit spawn rate
  /// </summary>
  [ExportGroup("Spawn Rate")]
  [Export(PropertyHint.Range, "0,10,0.01,or_greater")]
  public float FruitRate { get; set; } = 1.0f;

  /// <summary>
  /// Multiplier of the crop spawn rate
  /// </summary>
  [Export(PropertyHint.Range, "0,10,0.01,or_greater")]
  public float CropRate { get; set; } = 1.0f;

  /// <summary>
  /// Multiplier of the animal spawn rate
  /// </summary>
  [Export(PropertyHint.Range, "0,10,0.01,or_greater")]
  public float AnimalRate { get; set; } = 1.0f;

  /// <summary>
  /// Multiplier of the mineral spawn rate
  /// </summary>
  [Export(PropertyHint.Range, "0,10,0.01,or_greater")]
  public float MineralRate { get; set; } = 1.0f;

  /// <summary>
  /// Multiplier of the fish spawn rate
  /// </summary>
  [Export(PropertyHint.Range, "0,10,0.01,or_greater")]
  public float FishRate { get; set; } = 1.0f;

  /// <summary>
  /// Multiplier of the forest generation rate
  /// </summary>
  [ExportGroup("Terrain Rate")]
  [Export(PropertyHint.Range, "0,10,0.01,or_greater")]
  public float ForestRate { get; set; } = 1.0f;

  /// <summary>
  /// Multiplier of the mountain generation rate
  /// </summary>
  [Export(PropertyHint.Range, "0,10,0.01,or_greater")]
  public float MountainRate { get; set; } = 1.0f;

  /// <summary>
  /// Multiplier of the water generation rate
  /// </summary>
  [Export(PropertyHint.Range, "0,10,0.01,or_greater")]
  public float WaterRate { get; set; } = 1.0f;

  /// <summary>
  /// Builds the resource from a tribe entry
  /// </summary>
  /// <param name="type">the tribe type</param>
  /// <param name="tribe">the tribe</param>
  public static TribeResource FromData(TribeType type, Tribe tribe) {
    ArgumentNullException.ThrowIfNull(tribe);
    ArgumentNullException.ThrowIfNull(tribe.SpawnRate);
    ArgumentNullException.ThrowIfNull(tribe.TerrainRate);

    return new TribeResource {
      TribeType = type,
      StartingBranch = tribe.StartingBranch,
      TechOverrides = [.. (tribe.TechOverrides ?? []).Select(TechOverrideResource.FromData)],
      StartingStars = tribe.StartingStars,
      FruitRate = tribe.SpawnRate.FruitRate,
      CropRate = tribe.SpawnRate.CropRate,
      AnimalRate = tribe.SpawnRate.AnimalRate,
      MineralRate = tribe.SpawnRate.MineralRate,
      FishRate = tribe.SpawnRate.FishRate,
      ForestRate = tribe.TerrainRate.ForestRate,
      MountainRate = tribe.TerrainRate.MountainRate,
      WaterRate = tribe.TerrainRate.WaterRate
    };
  }

  /// <summary>
  /// Converts the resource back to a tribe entry
  /// </summary>
  /// <exception cref="InvalidOperationException">threw when an element of <see cref="TechOverrides"/> is empty</exception>
  public TribeSerializedData ToData() => new() {
    TribeType = TribeType,
    Tribe = new Tribe {
      StartingBranch = StartingBranch,
      // a tribe without unique techs doesn't declare the list at all
      TechOverrides = TechOverrides.Count == 0
        ? null
        : TechOverrides.Select((techOverride, i) =>
          (techOverride ?? throw new InvalidOperationException($"tech override {i} of {TribeType} is empty")).ToData())
          .ToList(),
      SpawnRate = new SpawnRate {
        FruitRate = FruitRate, CropRate = CropRate, AnimalRate = AnimalRate, MineralRate = MineralRate, FishRate = FishRate
      },
      TerrainRate = new TerrainRate { ForestRate = ForestRate, MountainRate = MountainRate, WaterRate = WaterRate },
      StartingStars = StartingStars
    }
  };
}
