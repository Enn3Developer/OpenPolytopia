namespace OpenPolytopia;

using System;
using System.Text.Json;
using System.Text.Unicode;
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
  private static readonly JsonSerializerOptions _jsonOptions = new() {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All),
    TypeInfoResolver = TribeGenerationContext.Default,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
  };

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
  /// Lower values produce more compact continents while higher values produce rougher coasts
  /// </remarks>
  [Export(PropertyHint.Range, "0,8,1")]
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
  /// can be called again to regenerate the map; emits <see cref="MapGenerated"/> when done
  /// </remarks>
  /// <exception cref="InvalidOperationException">
  /// if <see cref="PlayerTribes"/> is empty or has more than 15 players
  /// </exception>
  /// <example>
  /// <code>
  /// var node = GetNode&lt;TerrainGenerationNode&gt;("TerrainGenerationNode");
  /// await node.GenerateMapAsync();
  /// var grid = node.Grid!;
  /// </code>
  /// </example>
  public async Task GenerateMapAsync() {
    // Tile.Owner is 4 bits, so there can't be more than 15 players
    if (PlayerTribes.Length is 0 or > 15) {
      throw new InvalidOperationException(
        $"invalid number of players: {PlayerTribes.Length}; must be between 1 and 15");
    }

    if (TribeManager.Tribes.Count == 0) {
      RegisterEmbeddedTribes();
    }

    var players = new Player[PlayerTribes.Length];
    for (var i = 0; i < PlayerTribes.Length; i++) {
      players[i] = new Player((TribeType)PlayerTribes[i], i + 1);
    }

    Players = players;
    Grid = new Grid((uint)Math.Max(GridSize, 1));
    CityManager = new CityManager(Grid);

    var generation = new TerrainGeneration(Grid, CityManager, TribeManager, players,
      Seed == 0 ? null : Seed) {
      InitialLand = InitialLand, Smoothing = Smoothing, Relief = Relief, WaterRate = WaterRate
    };
    await generation.GenerateMapAsync();

    EmitSignal(SignalName.MapGenerated);
  }

  /// <summary>
  /// Registers the tribes from the embedded <c>tribes.json</c>
  /// </summary>
  private void RegisterEmbeddedTribes() {
    var tribes = JsonSerializer.Deserialize<TribesSerializedData>(EmbeddedResources.TribesData, _jsonOptions);
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
