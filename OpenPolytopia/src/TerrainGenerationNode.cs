namespace OpenPolytopia;

using System;
using System.Threading.Tasks;
using Common;
using Godot;

/// <summary>
/// Node wrapper around <see cref="Common.TerrainGeneration"/> so a map can be generated
/// directly from a scene
/// </summary>
/// <remarks>
/// Add it to a scene, configure the generation parameters from the inspector and either let it
/// generate the map when it's ready (see <see cref="GenerateOnReady"/>) or call
/// <see cref="GenerateMapAsync"/> yourself; the generated map is available through
/// <see cref="Grid"/> and <see cref="CityManager"/> once <see cref="MapGenerated"/> is emitted
/// </remarks>
[GlobalClass]
public partial class TerrainGenerationNode : Node {
  /// <summary>
  /// Emitted when the map has been generated
  /// </summary>
  [Signal]
  public delegate void MapGeneratedEventHandler();

  /// <summary>
  /// Width of the squared grid to generate
  /// </summary>
  [Export(PropertyHint.Range, "4,64,1")]
  public int GridSize { get; set; } = 16;

  /// <summary>
  /// Random seed used by the generation
  /// </summary>
  /// <remarks>
  /// If 0, a random seed is used
  /// </remarks>
  [Export]
  public int Seed { get; set; }

  /// <summary>
  /// Fraction of the map converted to land before the smoothing passes
  /// </summary>
  [Export(PropertyHint.Range, "0,1,0.01")]
  public float InitialLand { get; set; } = 0.5f;

  /// <summary>
  /// Number of cellular-automata smoothing passes applied to the initial land
  /// </summary>
  [Export(PropertyHint.Range, "0,8,1")]
  public int Smoothing { get; set; } = 3;

  /// <summary>
  /// Land/water balance of the smoothing passes
  /// </summary>
  /// <remarks>
  /// Lower values erode the land into the ocean while higher values expand it; the range is
  /// limited because values outside it erode or flood the whole map
  /// </remarks>
  [Export(PropertyHint.Range, "3,6,1")]
  public int Relief { get; set; } = 4;

  /// <summary>
  /// Base probability of a land tile converting to water, multiplied by the tribe's
  /// <see cref="TerrainRate.WaterRate"/>
  /// </summary>
  [Export(PropertyHint.Range, "0,1,0.01")]
  public float WaterRate { get; set; } = 0.05f;

  /// <summary>
  /// Tribes of the players as <see cref="TribeType"/> values
  /// </summary>
  /// <remarks>
  /// The player at index <c>i</c> gets id <c>i + 1</c>
  /// </remarks>
  [Export]
  public int[] PlayerTribes { get; set; } = [(int)TribeType.Imperius, (int)TribeType.Bardur];

  /// <summary>
  /// Whether to generate the map as soon as the node is ready
  /// </summary>
  [Export]
  public bool GenerateOnReady { get; set; } = true;

  /// <summary>
  /// The generated grid
  /// </summary>
  /// <remarks>
  /// Null until the first generation
  /// </remarks>
  public Grid? Grid { get; private set; }

  /// <summary>
  /// The city manager holding the generated cities and villages
  /// </summary>
  /// <remarks>
  /// Null until the first generation
  /// </remarks>
  public CityManager? CityManager { get; private set; }

  /// <summary>
  /// The players of the generated map
  /// </summary>
  /// <remarks>
  /// Null until the first generation
  /// </remarks>
  public Player[]? Players { get; private set; }

  // latest queued generation, so concurrent calls chain instead of racing each other
  private Task? _generation;

  /// <summary>
  /// The tribe manager used by the generation
  /// </summary>
  /// <remarks>
  /// If no tribe is registered when the generation starts, the tribes from the embedded
  /// <c>tribes.json</c> are registered automatically; set your own populated manager to
  /// override this behavior
  /// </remarks>
  public TribeManager TribeManager { get; set; } = new();

  public override void _Ready() {
    if (GenerateOnReady) {
      _ = GenerateAndReportAsync();
    }
  }

  /// <summary>
  /// Generates a new map with the current parameters
  /// </summary>
  /// <remarks>
  /// A fresh <see cref="Grid"/> and <see cref="CityManager"/> are created on every call, so it
  /// can be called again to regenerate the map; emits <see cref="MapGenerated"/> when done.
  /// <br/>
  /// If a generation is already running (for example the one started by
  /// <see cref="GenerateOnReady"/>), this waits for it to finish and then generates another
  /// map, so the parameters set before this call are always used
  /// </remarks>
  /// <exception cref="InvalidOperationException">
  /// if <see cref="PlayerTribes"/> is empty, has more than 15 players or contains an invalid tribe
  /// </exception>
  /// <example>
  /// <code>
  /// var node = GetNode&lt;TerrainGenerationNode&gt;("TerrainGenerationNode");
  /// await node.GenerateMapAsync();
  /// var grid = node.Grid!;
  /// </code>
  /// </example>
  public async Task GenerateMapAsync() {
    // queue this generation after the running one, so this call always generates a fresh map
    // with the current parameters instead of returning a map built from stale ones
    var current = GenerateAfterAsync(_generation);
    _generation = current;
    try {
      await current;
    }
    finally {
      // a queued generation may have replaced this one already
      if (_generation == current) {
        _generation = null;
      }
    }
  }

  private async Task GenerateAfterAsync(Task? previous) {
    if (previous != null) {
      try {
        await previous;
      }
      catch (Exception) {
        // the previous generation already reported its failure to its own caller
      }
    }

    await GenerateInternalAsync();
  }

  private async Task GenerateInternalAsync() {
    if (TribeManager.Tribes.Count == 0) {
      RegisterEmbeddedTribes();
    }

    // the players are validated by TerrainGeneration.GenerateMapAsync
    var players = new Player[PlayerTribes.Length];
    for (var i = 0; i < PlayerTribes.Length; i++) {
      players[i] = new Player((TribeType)PlayerTribes[i], i + 1);
    }

    var grid = new Grid((uint)Math.Max(GridSize, 1));
    var cityManager = new CityManager(grid);

    var generation = new TerrainGeneration(grid, cityManager, TribeManager, players,
      Seed == 0 ? null : Seed) {
      InitialLand = InitialLand, Smoothing = Smoothing, Relief = Relief, WaterRate = WaterRate
    };
    await generation.GenerateMapAsync();

    // publish the new map only when it's fully generated, so consumers never see a partial grid
    Players = players;
    Grid = grid;
    CityManager = cityManager;
    EmitSignal(SignalName.MapGenerated);
  }

  /// <summary>
  /// Registers the tribes from the embedded <c>tribes.json</c>
  /// </summary>
  private void RegisterEmbeddedTribes() {
    var tribes = EmbeddedResources.LoadTribes();
    if (tribes == null) {
      GD.PushWarning("no tribes data found; terrain generation will use the base rates");
      return;
    }

    TribeManager.RegisterTribes(tribes);
  }

  private async Task GenerateAndReportAsync() {
    try {
      await GenerateMapAsync();
    }
    catch (Exception e) {
      GD.PushError($"terrain generation failed: {e}");
    }
  }
}
