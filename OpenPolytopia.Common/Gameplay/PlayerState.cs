namespace OpenPolytopia.Common.Gameplay;

/// <summary>
/// The mutable state of a single player inside a <see cref="Game"/>
/// </summary>
/// <remarks>
/// Built by <see cref="Game"/> from the <see cref="Player"/> record and the tribe data it describes; only
/// <see cref="Game"/> is meant to build one, everyone else just reads and reacts to it
/// </remarks>
public class PlayerState {
  private int _stars;

  /// <summary>
  /// The player id, 1..16
  /// </summary>
  public int Id { get; }

  /// <summary>
  /// The tribe this player plays as
  /// </summary>
  public TribeType Tribe { get; }

  /// <summary>
  /// Current stars the player can spend
  /// </summary>
  /// <remarks>
  /// Clamped to 0: no action inside the engine can ever push this below zero, no matter what it's set to
  /// </remarks>
  public int Stars {
    get => _stars;
    set => _stars = value < 0 ? 0 : value;
  }

  /// <summary>
  /// This player's tech tree, with the starting node of their tribe already researched
  /// </summary>
  public TechTree TechTree { get; }

  /// <summary>
  /// This player's score
  /// </summary>
  public Score Score { get; } = new();

  /// <summary>
  /// Whether the player is still in the game
  /// </summary>
  /// <remarks>
  /// Set to false by <see cref="Game"/> once the player owns no city anymore; an eliminated player keeps their
  /// <see cref="PlayerState"/> around (for score/history) but is skipped by turn order
  /// </remarks>
  public bool Alive { get; set; } = true;

  /// <summary>
  /// Builds the state of a player at the start of a game
  /// </summary>
  /// <param name="id">the player id</param>
  /// <param name="tribe">the tribe this player plays as</param>
  /// <param name="startingStars">the starting stars, from <see cref="Tribe.StartingStars"/></param>
  /// <param name="techTree">the tech tree of this player, already built with the tribe's starting node researched</param>
  internal PlayerState(int id, TribeType tribe, int startingStars, TechTree techTree) {
    Id = id;
    Tribe = tribe;
    Stars = startingStars;
    TechTree = techTree;
  }
}
