namespace OpenPolytopia.Common.Gameplay;

/// <summary>
/// Result of a request made to a <see cref="Game"/>
/// </summary>
/// <remarks>
/// Every action on <see cref="Game"/> returns one of these instead of throwing, so a rejected action can be reported
/// back to a client without exceptions crossing the network boundary; <see cref="Ok"/> is always 0 so a caller can
/// check for success without knowing every other case
/// </remarks>
public enum GameActionResult : byte {
  /// <summary>The action succeeded</summary>
  Ok = 0,

  /// <summary>No game exists with the given id</summary>
  GameNotFound = 1,

  /// <summary>The player isn't part of this game</summary>
  NotInGame = 2,

  /// <summary>The game already ended</summary>
  GameOver = 3,

  /// <summary>It isn't this player's turn</summary>
  NotYourTurn = 4,

  /// <summary>The player has been eliminated</summary>
  PlayerEliminated = 5,

  /// <summary>The given position is outside the grid</summary>
  InvalidPosition = 6,

  /// <summary>There's no troop on the given tile</summary>
  NoTroop = 7,

  /// <summary>The troop doesn't belong to the player</summary>
  NotTroopOwner = 8,

  /// <summary>The troop already moved this turn</summary>
  TroopAlreadyMoved = 9,

  /// <summary>The troop already attacked this turn</summary>
  TroopAlreadyAttacked = 10,

  /// <summary>The troop already moved and attacked, so it can't act anymore this turn</summary>
  TroopAlreadyActed = 11,

  /// <summary>The target tile can't be reached by the troop</summary>
  Unreachable = 12,

  /// <summary>The troop can't attack (no attack power)</summary>
  CannotAttack = 13,

  /// <summary>There's no troop on the target tile</summary>
  NoTarget = 14,

  /// <summary>The target is out of the troop's attack range</summary>
  OutOfRange = 15,

  /// <summary>The target troop belongs to the acting player, not an enemy</summary>
  NotEnemy = 16,

  /// <summary>The player doesn't have enough stars for this action</summary>
  NotEnoughStars = 17,

  /// <summary>No tech node exists with the given id</summary>
  TechNotFound = 18,

  /// <summary>The tech node was already researched</summary>
  TechAlreadyResearched = 19,

  /// <summary>The tech node isn't unlocked yet (its parent isn't researched)</summary>
  TechLocked = 20,

  /// <summary>No building is registered with the given type</summary>
  InvalidBuilding = 21,

  /// <summary>The tile's kind doesn't match what the building requires</summary>
  WrongTile = 22,

  /// <summary>The tile isn't owned by the player</summary>
  TileNotOwned = 23,

  /// <summary>The tile doesn't belong to a city owned by the player</summary>
  TileNotInCity = 24,

  /// <summary>The tile already has something built on it</summary>
  TileOccupied = 25,

  /// <summary>The tile doesn't have the resource the building requires</summary>
  MissingResource = 26,

  /// <summary>Another tile of the same city already has this unique building</summary>
  BuildingAlreadyInCity = 27,

  /// <summary>The tile isn't a city</summary>
  NotACity = 28,

  /// <summary>The city isn't owned by the player</summary>
  CityNotOwned = 29,

  /// <summary>The city already has as many troops as its level allows</summary>
  CityFull = 30,

  /// <summary>The troop type can't be trained (no cost, missing tech, or unregistered)</summary>
  TroopNotTrainable = 31,

  /// <summary>The troop type isn't registered</summary>
  InvalidTroopType = 32,

  /// <summary>The tile isn't a valid capture target</summary>
  NotACaptureTarget = 33
}

/// <summary>
/// Serialization helpers for <see cref="GameActionResult"/>
/// </summary>
/// <remarks>
/// Follows the same single-byte pattern as <c>LobbyActionResultSerialization</c>
/// (<see cref="Network.Packets.LobbyActionResult"/>)
/// </remarks>
public static class GameActionResultSerialization {
  /// <summary>
  /// Serializes a <see cref="GameActionResult"/> as a single byte
  /// </summary>
  /// <param name="value">the value to serialize</param>
  /// <param name="bytes">the buffer to append to</param>
  public static void Serialize(this GameActionResult value, List<byte> bytes) => bytes.Add((byte)value);

  /// <summary>
  /// Deserializes a <see cref="GameActionResult"/> from a single byte
  /// </summary>
  /// <param name="value">the value to write the result into</param>
  /// <param name="bytes">the buffer to read from</param>
  /// <param name="index">the index to start reading from; advanced past the read byte</param>
  public static void Deserialize(this ref GameActionResult value, byte[] bytes, ref uint index) =>
    value = (GameActionResult)bytes[index++];
}
