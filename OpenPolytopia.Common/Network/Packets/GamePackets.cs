namespace OpenPolytopia.Common.Network.Packets;

using Godot;
using Gameplay;

/// <summary>
/// Asks the server for the full state of a game
/// </summary>
[GeneratedPacket]
public partial class GetGameStatePacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;
}

/// <summary>
/// Server response to <see cref="GetGameStatePacket"/>, the full state of a game
/// </summary>
/// <remarks>
/// <see cref="Tiles"/>[i] and <see cref="Troops"/>[i] are the packed <c>Tile.Raw</c> and <c>TroopData.Raw</c>
/// values for grid index i (0 in <see cref="Troops"/> means no troop on that tile)
/// </remarks>
[GeneratedPacket]
public partial class GameStatePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  [PacketField]
  public GameActionResult Result;

  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Side length of the square grid the game is played on
  /// </summary>
  [PacketField]
  public uint WorldSize;

  /// <summary>
  /// Current turn number
  /// </summary>
  [PacketField]
  public uint Turn;

  /// <summary>
  /// Id of the player whose turn it currently is
  /// </summary>
  [PacketField]
  public uint CurrentPlayer;

  /// <summary>
  /// Turn limit configured for the game; 0 means no limit
  /// </summary>
  [PacketField]
  public uint MaxTurns;

  /// <summary>
  /// Whether the game has ended
  /// </summary>
  [PacketField]
  public bool Over;

  /// <summary>
  /// Id of the winning player; valid only when <see cref="Over"/> is true
  /// </summary>
  [PacketField]
  public uint Winner;

  /// <summary>
  /// The public state of every player in the game
  /// </summary>
  [PacketField]
  public List<GamePlayerData> Players = [];

  /// <summary>
  /// Packed <c>Tile.Raw</c> value of every tile of the grid, indexed the same way as the grid
  /// </summary>
  [PacketField]
  public ulong[] Tiles = [];

  /// <summary>
  /// Packed <c>TroopData.Raw</c> value of every tile of the grid, indexed the same way as the grid; 0 means no troop
  /// </summary>
  [PacketField]
  public uint[] Troops = [];
}

/// <summary>
/// Asks the server to move a troop
/// </summary>
[GeneratedPacket]
public partial class MoveTroopPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Position of the troop to move
  /// </summary>
  [PacketField]
  public Vector2I From;

  /// <summary>
  /// Position to move the troop to
  /// </summary>
  [PacketField]
  public Vector2I To;
}

/// <summary>
/// Server response to <see cref="MoveTroopPacket"/>
/// </summary>
[GeneratedPacket]
public partial class MoveTroopResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  [PacketField]
  public GameActionResult Result;
}

/// <summary>
/// Broadcast to every player of a game when a troop moved
/// </summary>
[GeneratedPacket]
public partial class TroopMovedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Id of the player who moved the troop
  /// </summary>
  [PacketField]
  public uint PlayerId;

  /// <summary>
  /// Position the troop moved from
  /// </summary>
  [PacketField]
  public Vector2I From;

  /// <summary>
  /// Position the troop moved to
  /// </summary>
  [PacketField]
  public Vector2I To;

  /// <summary>
  /// The tiles and players that changed because of this move
  /// </summary>
  [PacketField]
  public GameUpdate Update = new();
}

/// <summary>
/// Asks the server to attack with a troop
/// </summary>
[GeneratedPacket]
public partial class AttackPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Position of the attacking troop
  /// </summary>
  [PacketField]
  public Vector2I From;

  /// <summary>
  /// Position of the targeted troop
  /// </summary>
  [PacketField]
  public Vector2I Target;
}

/// <summary>
/// Server response to <see cref="AttackPacket"/>
/// </summary>
[GeneratedPacket]
public partial class AttackResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  [PacketField]
  public GameActionResult Result;
}

/// <summary>
/// Broadcast to every player of a game when an attack was resolved
/// </summary>
[GeneratedPacket]
public partial class CombatPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Id of the player who attacked
  /// </summary>
  [PacketField]
  public uint PlayerId;

  /// <summary>
  /// Position the attacking troop attacked from
  /// </summary>
  [PacketField]
  public Vector2I From;

  /// <summary>
  /// Position of the attacked troop
  /// </summary>
  [PacketField]
  public Vector2I Target;

  /// <summary>
  /// Hp of the attacker after the attack; meaningless if <see cref="AttackerKilled"/> is true
  /// </summary>
  [PacketField]
  public uint AttackerHp;

  /// <summary>
  /// Hp of the defender after the attack; meaningless if <see cref="DefenderKilled"/> is true
  /// </summary>
  [PacketField]
  public uint DefenderHp;

  /// <summary>
  /// Whether the attacker died, e.g. to retaliation damage
  /// </summary>
  [PacketField]
  public bool AttackerKilled;

  /// <summary>
  /// Whether the defender died
  /// </summary>
  [PacketField]
  public bool DefenderKilled;

  /// <summary>
  /// Position of the attacker after the attack, e.g. it moved onto the defender's tile on a melee kill
  /// </summary>
  [PacketField]
  public Vector2I AttackerPosition;

  /// <summary>
  /// The tiles and players that changed because of this attack
  /// </summary>
  [PacketField]
  public GameUpdate Update = new();
}

/// <summary>
/// Asks the server to train a troop in a city
/// </summary>
[GeneratedPacket]
public partial class TrainTroopPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Position of the city to train the troop in
  /// </summary>
  [PacketField]
  public Vector2I City;

  /// <summary>
  /// Type of troop to train, i.e. <c>TroopType</c>
  /// </summary>
  [PacketField]
  public uint TroopType;
}

/// <summary>
/// Server response to <see cref="TrainTroopPacket"/>
/// </summary>
[GeneratedPacket]
public partial class TrainTroopResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  [PacketField]
  public GameActionResult Result;
}

/// <summary>
/// Broadcast to every player of a game when a troop was trained
/// </summary>
[GeneratedPacket]
public partial class TroopTrainedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Id of the player who trained the troop
  /// </summary>
  [PacketField]
  public uint PlayerId;

  /// <summary>
  /// Position the troop was trained on
  /// </summary>
  [PacketField]
  public Vector2I Position;

  /// <summary>
  /// Type of the trained troop, i.e. <c>TroopType</c>
  /// </summary>
  [PacketField]
  public uint TroopType;

  /// <summary>
  /// The tiles and players that changed because of this training
  /// </summary>
  [PacketField]
  public GameUpdate Update = new();
}

/// <summary>
/// Asks the server to research a tech
/// </summary>
[GeneratedPacket]
public partial class ResearchTechPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Id of the tech node to research
  /// </summary>
  [PacketField]
  public string TechId = "";
}

/// <summary>
/// Server response to <see cref="ResearchTechPacket"/>
/// </summary>
[GeneratedPacket]
public partial class ResearchTechResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  [PacketField]
  public GameActionResult Result;
}

/// <summary>
/// Broadcast to every player of a game when a tech was researched
/// </summary>
[GeneratedPacket]
public partial class TechResearchedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Id of the player who researched the tech
  /// </summary>
  [PacketField]
  public uint PlayerId;

  /// <summary>
  /// Id of the researched tech node
  /// </summary>
  [PacketField]
  public string TechId = "";

  /// <summary>
  /// The tiles and players that changed because of this research
  /// </summary>
  [PacketField]
  public GameUpdate Update = new();
}

/// <summary>
/// Asks the server to build on, or harvest, a tile
/// </summary>
[GeneratedPacket]
public partial class BuildPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Position of the tile to build on
  /// </summary>
  [PacketField]
  public Vector2I Position;

  /// <summary>
  /// Type of building to build, i.e. <c>BuildingType</c>
  /// </summary>
  [PacketField]
  public uint Building;
}

/// <summary>
/// Server response to <see cref="BuildPacket"/>
/// </summary>
[GeneratedPacket]
public partial class BuildResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  [PacketField]
  public GameActionResult Result;
}

/// <summary>
/// Broadcast to every player of a game when a building was built
/// </summary>
[GeneratedPacket]
public partial class BuildingBuiltPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Id of the player who built
  /// </summary>
  [PacketField]
  public uint PlayerId;

  /// <summary>
  /// Position the building was built on
  /// </summary>
  [PacketField]
  public Vector2I Position;

  /// <summary>
  /// Type of the built building, i.e. <c>BuildingType</c>
  /// </summary>
  [PacketField]
  public uint Building;

  /// <summary>
  /// The tiles and players that changed because of this build
  /// </summary>
  [PacketField]
  public GameUpdate Update = new();
}

/// <summary>
/// Asks the server to capture the city on a tile
/// </summary>
[GeneratedPacket]
public partial class CapturePacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Position of the city to capture
  /// </summary>
  [PacketField]
  public Vector2I Position;
}

/// <summary>
/// Server response to <see cref="CapturePacket"/>
/// </summary>
[GeneratedPacket]
public partial class CaptureResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  [PacketField]
  public GameActionResult Result;
}

/// <summary>
/// Broadcast to every player of a game when a city was captured
/// </summary>
[GeneratedPacket]
public partial class CityCapturedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Id of the player who captured the city
  /// </summary>
  [PacketField]
  public uint PlayerId;

  /// <summary>
  /// Position of the captured city
  /// </summary>
  [PacketField]
  public Vector2I Position;

  /// <summary>
  /// Id of the player who owned the city before the capture; 0 if it was neutral
  /// </summary>
  [PacketField]
  public uint PreviousOwner;

  /// <summary>
  /// Id of the player eliminated by this capture; 0 if nobody got eliminated
  /// </summary>
  [PacketField]
  public uint EliminatedPlayer;

  /// <summary>
  /// The tiles and players that changed because of this capture
  /// </summary>
  [PacketField]
  public GameUpdate Update = new();
}

/// <summary>
/// Asks the server to end the sender's turn
/// </summary>
[GeneratedPacket]
public partial class EndTurnPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;
}

/// <summary>
/// Server response to <see cref="EndTurnPacket"/>
/// </summary>
[GeneratedPacket]
public partial class EndTurnResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  [PacketField]
  public GameActionResult Result;
}

/// <summary>
/// Broadcast to every player of a game when a new turn began
/// </summary>
[GeneratedPacket]
public partial class TurnStartedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// The new turn number
  /// </summary>
  [PacketField]
  public uint Turn;

  /// <summary>
  /// Id of the player whose turn began
  /// </summary>
  [PacketField]
  public uint PlayerId;

  /// <summary>
  /// The tiles and players that changed at the start of this turn
  /// </summary>
  [PacketField]
  public GameUpdate Update = new();
}

/// <summary>
/// Broadcast to every player of a game when a player was eliminated
/// </summary>
[GeneratedPacket]
public partial class PlayerEliminatedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Id of the eliminated player
  /// </summary>
  [PacketField]
  public uint PlayerId;

  /// <summary>
  /// The tiles and players that changed because of this elimination
  /// </summary>
  [PacketField]
  public GameUpdate Update = new();
}

/// <summary>
/// Broadcast to every player of a game when it ends
/// </summary>
[GeneratedPacket]
public partial class GameOverPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  [PacketField]
  public ulong GameId;

  /// <summary>
  /// Id of the winning player
  /// </summary>
  [PacketField]
  public uint Winner;

  /// <summary>
  /// The final public state of every player in the game
  /// </summary>
  [PacketField]
  public List<GamePlayerData> Players = [];
}
