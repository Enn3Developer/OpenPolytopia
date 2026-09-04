namespace OpenPolytopia.Common.Network.Packets;

using Godot;
using Gameplay;

/// <summary>
/// Asks the server for the full state of a game
/// </summary>
public class GetGameStatePacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  public void Serialize(List<byte> bytes) => GameId.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => GameId.Deserialize(bytes, ref index);
}

/// <summary>
/// Server response to <see cref="GetGameStatePacket"/>, the full state of a game
/// </summary>
/// <remarks>
/// <see cref="Tiles"/>[i] and <see cref="Troops"/>[i] are the packed <c>Tile.Raw</c> and <c>TroopData.Raw</c>
/// values for grid index i (0 in <see cref="Troops"/> means no troop on that tile)
/// </remarks>
public class GameStatePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  public GameActionResult Result;

  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Side length of the square grid the game is played on
  /// </summary>
  public uint WorldSize;

  /// <summary>
  /// Current turn number
  /// </summary>
  public uint Turn;

  /// <summary>
  /// Id of the player whose turn it currently is
  /// </summary>
  public uint CurrentPlayer;

  /// <summary>
  /// Turn limit configured for the game; 0 means no limit
  /// </summary>
  public uint MaxTurns;

  /// <summary>
  /// Whether the game has ended
  /// </summary>
  public bool Over;

  /// <summary>
  /// Id of the winning player; valid only when <see cref="Over"/> is true
  /// </summary>
  public uint Winner;

  /// <summary>
  /// The public state of every player in the game
  /// </summary>
  public List<GamePlayerData> Players = [];

  /// <summary>
  /// Packed <c>Tile.Raw</c> value of every tile of the grid, indexed the same way as the grid
  /// </summary>
  public ulong[] Tiles = [];

  /// <summary>
  /// Packed <c>TroopData.Raw</c> value of every tile of the grid, indexed the same way as the grid; 0 means no troop
  /// </summary>
  public uint[] Troops = [];

  public void Serialize(List<byte> bytes) {
    Result.Serialize(bytes);
    GameId.Serialize(bytes);
    WorldSize.Serialize(bytes);
    Turn.Serialize(bytes);
    CurrentPlayer.Serialize(bytes);
    MaxTurns.Serialize(bytes);
    Over.Serialize(bytes);
    Winner.Serialize(bytes);
    Players.Serialize(bytes);
    Tiles.Serialize(bytes);
    Troops.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    Result.Deserialize(bytes, ref index);
    GameId.Deserialize(bytes, ref index);
    WorldSize.Deserialize(bytes, ref index);
    Turn.Deserialize(bytes, ref index);
    CurrentPlayer.Deserialize(bytes, ref index);
    MaxTurns.Deserialize(bytes, ref index);
    Over.Deserialize(bytes, ref index);
    Winner.Deserialize(bytes, ref index);
    Players.Deserialize(bytes, ref index);
    Tiles = ULongArraySerialization.Read(bytes, ref index);
    Troops = UIntArraySerialization.Read(bytes, ref index);
  }
}

/// <summary>
/// Asks the server to move a troop
/// </summary>
public class MoveTroopPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Position of the troop to move
  /// </summary>
  public Vector2I From;

  /// <summary>
  /// Position to move the troop to
  /// </summary>
  public Vector2I To;

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    From.Serialize(bytes);
    To.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    From.Deserialize(bytes, ref index);
    To.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Server response to <see cref="MoveTroopPacket"/>
/// </summary>
public class MoveTroopResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  public GameActionResult Result;

  public void Serialize(List<byte> bytes) => Result.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Result.Deserialize(bytes, ref index);
}

/// <summary>
/// Broadcast to every player of a game when a troop moved
/// </summary>
public class TroopMovedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Id of the player who moved the troop
  /// </summary>
  public uint PlayerId;

  /// <summary>
  /// Position the troop moved from
  /// </summary>
  public Vector2I From;

  /// <summary>
  /// Position the troop moved to
  /// </summary>
  public Vector2I To;

  /// <summary>
  /// The tiles and players that changed because of this move
  /// </summary>
  public GameUpdate Update = new();

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    PlayerId.Serialize(bytes);
    From.Serialize(bytes);
    To.Serialize(bytes);
    Update.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    PlayerId.Deserialize(bytes, ref index);
    From.Deserialize(bytes, ref index);
    To.Deserialize(bytes, ref index);
    Update.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Asks the server to attack with a troop
/// </summary>
public class AttackPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Position of the attacking troop
  /// </summary>
  public Vector2I From;

  /// <summary>
  /// Position of the targeted troop
  /// </summary>
  public Vector2I Target;

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    From.Serialize(bytes);
    Target.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    From.Deserialize(bytes, ref index);
    Target.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Server response to <see cref="AttackPacket"/>
/// </summary>
public class AttackResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  public GameActionResult Result;

  public void Serialize(List<byte> bytes) => Result.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Result.Deserialize(bytes, ref index);
}

/// <summary>
/// Broadcast to every player of a game when an attack was resolved
/// </summary>
public class CombatPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Id of the player who attacked
  /// </summary>
  public uint PlayerId;

  /// <summary>
  /// Position the attacking troop attacked from
  /// </summary>
  public Vector2I From;

  /// <summary>
  /// Position of the attacked troop
  /// </summary>
  public Vector2I Target;

  /// <summary>
  /// Hp of the attacker after the attack; meaningless if <see cref="AttackerKilled"/> is true
  /// </summary>
  public uint AttackerHp;

  /// <summary>
  /// Hp of the defender after the attack; meaningless if <see cref="DefenderKilled"/> is true
  /// </summary>
  public uint DefenderHp;

  /// <summary>
  /// Whether the attacker died, e.g. to retaliation damage
  /// </summary>
  public bool AttackerKilled;

  /// <summary>
  /// Whether the defender died
  /// </summary>
  public bool DefenderKilled;

  /// <summary>
  /// Position of the attacker after the attack, e.g. it moved onto the defender's tile on a melee kill
  /// </summary>
  public Vector2I AttackerPosition;

  /// <summary>
  /// The tiles and players that changed because of this attack
  /// </summary>
  public GameUpdate Update = new();

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    PlayerId.Serialize(bytes);
    From.Serialize(bytes);
    Target.Serialize(bytes);
    AttackerHp.Serialize(bytes);
    DefenderHp.Serialize(bytes);
    AttackerKilled.Serialize(bytes);
    DefenderKilled.Serialize(bytes);
    AttackerPosition.Serialize(bytes);
    Update.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    PlayerId.Deserialize(bytes, ref index);
    From.Deserialize(bytes, ref index);
    Target.Deserialize(bytes, ref index);
    AttackerHp.Deserialize(bytes, ref index);
    DefenderHp.Deserialize(bytes, ref index);
    AttackerKilled.Deserialize(bytes, ref index);
    DefenderKilled.Deserialize(bytes, ref index);
    AttackerPosition.Deserialize(bytes, ref index);
    Update.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Asks the server to train a troop in a city
/// </summary>
public class TrainTroopPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Position of the city to train the troop in
  /// </summary>
  public Vector2I City;

  /// <summary>
  /// Type of troop to train, i.e. <c>TroopType</c>
  /// </summary>
  public uint TroopType;

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    City.Serialize(bytes);
    TroopType.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    City.Deserialize(bytes, ref index);
    TroopType.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Server response to <see cref="TrainTroopPacket"/>
/// </summary>
public class TrainTroopResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  public GameActionResult Result;

  public void Serialize(List<byte> bytes) => Result.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Result.Deserialize(bytes, ref index);
}

/// <summary>
/// Broadcast to every player of a game when a troop was trained
/// </summary>
public class TroopTrainedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Id of the player who trained the troop
  /// </summary>
  public uint PlayerId;

  /// <summary>
  /// Position the troop was trained on
  /// </summary>
  public Vector2I Position;

  /// <summary>
  /// Type of the trained troop, i.e. <c>TroopType</c>
  /// </summary>
  public uint TroopType;

  /// <summary>
  /// The tiles and players that changed because of this training
  /// </summary>
  public GameUpdate Update = new();

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    PlayerId.Serialize(bytes);
    Position.Serialize(bytes);
    TroopType.Serialize(bytes);
    Update.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    PlayerId.Deserialize(bytes, ref index);
    Position.Deserialize(bytes, ref index);
    TroopType.Deserialize(bytes, ref index);
    Update.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Asks the server to research a tech
/// </summary>
public class ResearchTechPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Id of the tech node to research
  /// </summary>
  public string TechId = "";

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    TechId.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    TechId = StringSerialization.Read(bytes, ref index);
  }
}

/// <summary>
/// Server response to <see cref="ResearchTechPacket"/>
/// </summary>
public class ResearchTechResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  public GameActionResult Result;

  public void Serialize(List<byte> bytes) => Result.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Result.Deserialize(bytes, ref index);
}

/// <summary>
/// Broadcast to every player of a game when a tech was researched
/// </summary>
public class TechResearchedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Id of the player who researched the tech
  /// </summary>
  public uint PlayerId;

  /// <summary>
  /// Id of the researched tech node
  /// </summary>
  public string TechId = "";

  /// <summary>
  /// The tiles and players that changed because of this research
  /// </summary>
  public GameUpdate Update = new();

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    PlayerId.Serialize(bytes);
    TechId.Serialize(bytes);
    Update.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    PlayerId.Deserialize(bytes, ref index);
    TechId = StringSerialization.Read(bytes, ref index);
    Update.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Asks the server to build on, or harvest, a tile
/// </summary>
public class BuildPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Position of the tile to build on
  /// </summary>
  public Vector2I Position;

  /// <summary>
  /// Type of building to build, i.e. <c>BuildingType</c>
  /// </summary>
  public uint Building;

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    Position.Serialize(bytes);
    Building.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    Position.Deserialize(bytes, ref index);
    Building.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Server response to <see cref="BuildPacket"/>
/// </summary>
public class BuildResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  public GameActionResult Result;

  public void Serialize(List<byte> bytes) => Result.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Result.Deserialize(bytes, ref index);
}

/// <summary>
/// Broadcast to every player of a game when a building was built
/// </summary>
public class BuildingBuiltPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Id of the player who built
  /// </summary>
  public uint PlayerId;

  /// <summary>
  /// Position the building was built on
  /// </summary>
  public Vector2I Position;

  /// <summary>
  /// Type of the built building, i.e. <c>BuildingType</c>
  /// </summary>
  public uint Building;

  /// <summary>
  /// The tiles and players that changed because of this build
  /// </summary>
  public GameUpdate Update = new();

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    PlayerId.Serialize(bytes);
    Position.Serialize(bytes);
    Building.Serialize(bytes);
    Update.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    PlayerId.Deserialize(bytes, ref index);
    Position.Deserialize(bytes, ref index);
    Building.Deserialize(bytes, ref index);
    Update.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Asks the server to capture the city on a tile
/// </summary>
public class CapturePacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Position of the city to capture
  /// </summary>
  public Vector2I Position;

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    Position.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    Position.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Server response to <see cref="CapturePacket"/>
/// </summary>
public class CaptureResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  public GameActionResult Result;

  public void Serialize(List<byte> bytes) => Result.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Result.Deserialize(bytes, ref index);
}

/// <summary>
/// Broadcast to every player of a game when a city was captured
/// </summary>
public class CityCapturedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Id of the player who captured the city
  /// </summary>
  public uint PlayerId;

  /// <summary>
  /// Position of the captured city
  /// </summary>
  public Vector2I Position;

  /// <summary>
  /// Id of the player who owned the city before the capture; 0 if it was neutral
  /// </summary>
  public uint PreviousOwner;

  /// <summary>
  /// Id of the player eliminated by this capture; 0 if nobody got eliminated
  /// </summary>
  public uint EliminatedPlayer;

  /// <summary>
  /// The tiles and players that changed because of this capture
  /// </summary>
  public GameUpdate Update = new();

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    PlayerId.Serialize(bytes);
    Position.Serialize(bytes);
    PreviousOwner.Serialize(bytes);
    EliminatedPlayer.Serialize(bytes);
    Update.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    PlayerId.Deserialize(bytes, ref index);
    Position.Deserialize(bytes, ref index);
    PreviousOwner.Deserialize(bytes, ref index);
    EliminatedPlayer.Deserialize(bytes, ref index);
    Update.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Asks the server to end the sender's turn
/// </summary>
public class EndTurnPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  public void Serialize(List<byte> bytes) => GameId.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => GameId.Deserialize(bytes, ref index);
}

/// <summary>
/// Server response to <see cref="EndTurnPacket"/>
/// </summary>
public class EndTurnResponsePacket : IPacket {
  /// <summary>
  /// Result of the request
  /// </summary>
  public GameActionResult Result;

  public void Serialize(List<byte> bytes) => Result.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Result.Deserialize(bytes, ref index);
}

/// <summary>
/// Broadcast to every player of a game when a new turn began
/// </summary>
public class TurnStartedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// The new turn number
  /// </summary>
  public uint Turn;

  /// <summary>
  /// Id of the player whose turn began
  /// </summary>
  public uint PlayerId;

  /// <summary>
  /// The tiles and players that changed at the start of this turn
  /// </summary>
  public GameUpdate Update = new();

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    Turn.Serialize(bytes);
    PlayerId.Serialize(bytes);
    Update.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    Turn.Deserialize(bytes, ref index);
    PlayerId.Deserialize(bytes, ref index);
    Update.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Broadcast to every player of a game when a player was eliminated
/// </summary>
public class PlayerEliminatedPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Id of the eliminated player
  /// </summary>
  public uint PlayerId;

  /// <summary>
  /// The tiles and players that changed because of this elimination
  /// </summary>
  public GameUpdate Update = new();

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    PlayerId.Serialize(bytes);
    Update.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    PlayerId.Deserialize(bytes, ref index);
    Update.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Broadcast to every player of a game when it ends
/// </summary>
public class GameOverPacket : IPacket {
  /// <summary>
  /// Id of the game, i.e. the id of the lobby it started from
  /// </summary>
  public ulong GameId;

  /// <summary>
  /// Id of the winning player
  /// </summary>
  public uint Winner;

  /// <summary>
  /// The final public state of every player in the game
  /// </summary>
  public List<GamePlayerData> Players = [];

  public void Serialize(List<byte> bytes) {
    GameId.Serialize(bytes);
    Winner.Serialize(bytes);
    Players.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    GameId.Deserialize(bytes, ref index);
    Winner.Deserialize(bytes, ref index);
    Players.Deserialize(bytes, ref index);
  }
}
