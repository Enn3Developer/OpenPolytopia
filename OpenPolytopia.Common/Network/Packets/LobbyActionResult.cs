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

public static class LobbyActionResultSerialization {
  public static void Serialize(this LobbyActionResult value, List<byte> bytes) => bytes.Add((byte)value);

  public static void Deserialize(this ref LobbyActionResult value, byte[] bytes, ref uint index) =>
    value = (LobbyActionResult)bytes[index++];
}
