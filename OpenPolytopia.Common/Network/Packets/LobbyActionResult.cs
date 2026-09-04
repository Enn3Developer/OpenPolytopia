namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Result of a lobby-related request
/// </summary>
public enum LobbyActionResult : byte {
  Ok = 0,
  NotRegistered = 1,
  LobbyNotFound = 2,
  LobbyAlreadyStarted = 3,
  LobbyFull = 4,
  AlreadyJoinedLobby = 5,
  NotInLobby = 6,
  InvalidParameters = 7,
  TooManyLobbies = 8,
}
