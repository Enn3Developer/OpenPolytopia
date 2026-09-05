namespace OpenPolytopia.Common.Gameplay;

using System.Collections.Generic;

/// <summary>
/// A lossless capture of everything a <see cref="Game"/> mutates while it's played
/// </summary>
/// <remarks>
/// Only mutable state lives here: the content a game is built from (troop definitions, building definitions, tribes
/// and the shape of the tech tree) isn't captured because it never changes during a match, so
/// <see cref="Game.Restore"/> takes it back from the caller
/// <br/>
/// Every member is a primitive, an array of primitives or a list of <see cref="PlayerSnapshot"/>, so a row of a
/// SQLite table maps one to one onto it: the two big arrays go in <c>BLOB</c> columns through
/// <see cref="SnapshotEncoding"/>, the players in a child table keyed by turn order
/// </remarks>
public sealed class GameSnapshot {
  /// <summary>
  /// The layout version written by <see cref="Game.ToSnapshot"/>
  /// </summary>
  /// <remarks>
  /// Bumped whenever the meaning of a member or of a packed raw value changes, so a snapshot written by an older
  /// build is rejected instead of silently restoring a wrong game
  /// </remarks>
  public const int CURRENT_VERSION = 1;

  /// <summary>
  /// The layout version this snapshot was written with, see <see cref="CURRENT_VERSION"/>
  /// </summary>
  public required int Version { get; init; }

  /// <summary>
  /// The width (and height) of the grid
  /// </summary>
  public required uint GridSize { get; init; }

  /// <summary>
  /// The packed representation of every tile, indexed like <see cref="Grid.this[uint]"/>
  /// </summary>
  /// <remarks>
  /// This carries the custom data of the tile too, so the internals of every city (level, population, troops, parks,
  /// wall, forge, capital and connected flags) ride along with it
  /// </remarks>
  public required ulong[] Tiles { get; init; }

  /// <summary>
  /// The packed representation of every troop, indexed like <see cref="TroopManager.this[uint]"/>
  /// </summary>
  /// <remarks>
  /// A 0 is an empty tile, see <see cref="TroopData.IsValid"/>
  /// </remarks>
  public required uint[] Troops { get; init; }

  /// <summary>
  /// The grid index of every registered city, ordered by city id
  /// </summary>
  /// <remarks>
  /// The city with id <c>i</c> is at position <c>i - 1</c>, exactly like <see cref="CityManager.Cities"/>
  /// </remarks>
  public required uint[] CityIndexes { get; init; }

  /// <summary>
  /// Every player, in turn order
  /// </summary>
  public required IReadOnlyList<PlayerSnapshot> Players { get; init; }

  /// <summary>
  /// <see cref="GameSettings.MaxTurns"/> of the game
  /// </summary>
  public required uint MaxTurns { get; init; }

  /// <summary>
  /// <see cref="Game.Turn"/> of the game
  /// </summary>
  public required uint Turn { get; init; }

  /// <summary>
  /// <see cref="Game.CurrentPlayer"/> of the game
  /// </summary>
  public required int CurrentPlayer { get; init; }

  /// <summary>
  /// <see cref="Game.Started"/> of the game
  /// </summary>
  public required bool Started { get; init; }

  /// <summary>
  /// <see cref="Game.Over"/> of the game
  /// </summary>
  public required bool Over { get; init; }

  /// <summary>
  /// <see cref="Game.Winner"/> of the game
  /// </summary>
  public required int Winner { get; init; }
}

/// <summary>
/// A lossless capture of a single <see cref="PlayerState"/>
/// </summary>
/// <remarks>
/// The position of a player in <see cref="GameSnapshot.Players"/> is their turn order, so it has to be preserved by
/// whoever stores it: a SQLite child table needs an explicit order column, a plain autoincrement id isn't enough
/// </remarks>
public sealed class PlayerSnapshot {
  /// <summary>
  /// <see cref="PlayerState.Id"/> of the player, 1..16
  /// </summary>
  public required int Id { get; init; }

  /// <summary>
  /// <see cref="PlayerState.Tribe"/> of the player
  /// </summary>
  public required TribeType Tribe { get; init; }

  /// <summary>
  /// <see cref="PlayerState.Stars"/> of the player
  /// </summary>
  public required int Stars { get; init; }

  /// <summary>
  /// <see cref="Score.ScoreValue"/> of the player
  /// </summary>
  public required int Score { get; init; }

  /// <summary>
  /// <see cref="PlayerState.Alive"/> of the player
  /// </summary>
  public required bool Alive { get; init; }

  /// <summary>
  /// The ids of every node this player researched, as returned by <see cref="TechTree.ResearchedIds"/>
  /// </summary>
  /// <remarks>
  /// The tech tree itself isn't captured: it's rebuilt from the definition and the overrides of the tribe, which are
  /// content, and only the researched state is player state
  /// </remarks>
  public required IReadOnlyList<string> ResearchedTechs { get; init; }
}
