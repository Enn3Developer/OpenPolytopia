namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Asks the server for the list of all the lobbies
/// </summary>
public class GetLobbiesPacket : IPacket {
  public void Serialize(List<byte> bytes) { }

  public void Deserialize(byte[] bytes, ref uint index) { }
}

/// <summary>
/// Server response to <see cref="GetLobbiesPacket"/>
/// </summary>
public class GetLobbiesResponsePacket : IPacket {
  /// <summary>
  /// All the lobbies currently on the server
  /// </summary>
  public List<LobbyData> Lobbies = [];

  public void Serialize(List<byte> bytes) => Lobbies.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Lobbies.Deserialize(bytes, ref index);
}

/// <summary>
/// Creates a new lobby
/// </summary>
/// <remarks>
/// The sender automatically joins the new lobby
/// </remarks>
public class CreateLobbyPacket : IPacket {
  /// <summary>
  /// Number of max players that can join the lobby
  /// </summary>
  public uint MaxPlayers;

  /// <summary>
  /// Size of the world the game will be played on
  /// </summary>
  public uint WorldSize;

  /// <summary>
  /// Tribe chosen by the player creating the lobby
  /// </summary>
  public uint Tribe;

  public void Serialize(List<byte> bytes) {
    MaxPlayers.Serialize(bytes);
    WorldSize.Serialize(bytes);
    Tribe.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    MaxPlayers.Deserialize(bytes, ref index);
    WorldSize.Deserialize(bytes, ref index);
    Tribe.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Server response to <see cref="CreateLobbyPacket"/>
/// </summary>
public class CreateLobbyResponsePacket : IPacket {
  /// <summary>
  /// Result of the operation
  /// </summary>
  public LobbyActionResult Result;

  /// <summary>
  /// Id of the newly created lobby
  /// </summary>
  /// <remarks>
  /// Valid only if <see cref="Result"/> is <see cref="LobbyActionResult.Ok"/>
  /// </remarks>
  public ulong LobbyId;

  public void Serialize(List<byte> bytes) {
    Result.Serialize(bytes);
    LobbyId.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    Result.Deserialize(bytes, ref index);
    LobbyId.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Joins an existing lobby
/// </summary>
public class JoinLobbyPacket : IPacket {
  /// <summary>
  /// Id of the lobby to join
  /// </summary>
  public ulong LobbyId;

  /// <summary>
  /// Tribe chosen by the player
  /// </summary>
  public uint Tribe;

  public void Serialize(List<byte> bytes) {
    LobbyId.Serialize(bytes);
    Tribe.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    LobbyId.Deserialize(bytes, ref index);
    Tribe.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Server response to <see cref="JoinLobbyPacket"/>
/// </summary>
public class JoinLobbyResponsePacket : IPacket {
  /// <summary>
  /// Result of the operation
  /// </summary>
  public LobbyActionResult Result;

  /// <summary>
  /// Id of the lobby the player tried to join
  /// </summary>
  public ulong LobbyId;

  public void Serialize(List<byte> bytes) {
    Result.Serialize(bytes);
    LobbyId.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    Result.Deserialize(bytes, ref index);
    LobbyId.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Leaves a lobby the player joined before
/// </summary>
public class LeaveLobbyPacket : IPacket {
  /// <summary>
  /// Id of the lobby to leave
  /// </summary>
  public ulong LobbyId;

  public void Serialize(List<byte> bytes) => LobbyId.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => LobbyId.Deserialize(bytes, ref index);
}

/// <summary>
/// Server response to <see cref="LeaveLobbyPacket"/>
/// </summary>
public class LeaveLobbyResponsePacket : IPacket {
  /// <summary>
  /// Result of the operation
  /// </summary>
  public LobbyActionResult Result;

  /// <summary>
  /// Id of the lobby the player tried to leave
  /// </summary>
  public ulong LobbyId;

  public void Serialize(List<byte> bytes) {
    Result.Serialize(bytes);
    LobbyId.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    Result.Deserialize(bytes, ref index);
    LobbyId.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Updates the ready state of the player in a lobby
/// </summary>
/// <remarks>
/// When all the players in a lobby are ready, the game starts
/// </remarks>
public class SetReadyPacket : IPacket {
  /// <summary>
  /// Id of the lobby
  /// </summary>
  public ulong LobbyId;

  /// <summary>
  /// true to mark the player as ready, false to remove the ready state
  /// </summary>
  public bool Ready;

  public void Serialize(List<byte> bytes) {
    LobbyId.Serialize(bytes);
    Ready.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    LobbyId.Deserialize(bytes, ref index);
    Ready.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Server response to <see cref="SetReadyPacket"/>
/// </summary>
public class SetReadyResponsePacket : IPacket {
  /// <summary>
  /// Result of the operation
  /// </summary>
  public LobbyActionResult Result;

  /// <summary>
  /// Id of the lobby
  /// </summary>
  public ulong LobbyId;

  public void Serialize(List<byte> bytes) {
    Result.Serialize(bytes);
    LobbyId.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    Result.Deserialize(bytes, ref index);
    LobbyId.Deserialize(bytes, ref index);
  }
}

/// <summary>
/// Broadcast by the server when a lobby gets created or modified
/// </summary>
public class LobbyUpdatedPacket : IPacket {
  /// <summary>
  /// The new state of the lobby
  /// </summary>
  public LobbyData Lobby = new();

  public void Serialize(List<byte> bytes) => Lobby.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Lobby.Deserialize(bytes, ref index);
}

/// <summary>
/// Broadcast by the server when a lobby gets deleted
/// </summary>
public class LobbyDeletedPacket : IPacket {
  /// <summary>
  /// Id of the deleted lobby
  /// </summary>
  public ulong LobbyId;

  public void Serialize(List<byte> bytes) => LobbyId.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => LobbyId.Deserialize(bytes, ref index);
}

/// <summary>
/// Sent by the server to every player of a lobby when its game starts
/// </summary>
public class GameStartedPacket : IPacket {
  /// <summary>
  /// Id of the lobby whose game started
  /// </summary>
  public ulong LobbyId;

  /// <summary>
  /// The players taking part in the game
  /// </summary>
  public List<LobbyPlayerData> Players = [];

  public void Serialize(List<byte> bytes) {
    LobbyId.Serialize(bytes);
    Players.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    LobbyId.Deserialize(bytes, ref index);
    Players.Deserialize(bytes, ref index);
  }
}
