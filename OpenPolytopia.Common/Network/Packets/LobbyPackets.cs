namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Asks the server for the list of all the lobbies
/// </summary>
[GeneratedPacket]
public partial class GetLobbiesPacket : IPacket { }

/// <summary>
/// Server response to <see cref="GetLobbiesPacket"/>
/// </summary>
[GeneratedPacket]
public partial class GetLobbiesResponsePacket : IPacket {
  /// <summary>
  /// All the lobbies currently on the server
  /// </summary>
  [PacketField]
  public List<LobbyData> Lobbies { get; set; } = [];
}

/// <summary>
/// Creates a new lobby
/// </summary>
/// <remarks>
/// The sender automatically joins the new lobby
/// </remarks>
[GeneratedPacket]
public partial class CreateLobbyPacket : IPacket {
  /// <summary>
  /// Number of max players that can join the lobby
  /// </summary>
  [PacketField]
  public uint MaxPlayers;

  /// <summary>
  /// Size of the world the game will be played on
  /// </summary>
  [PacketField]
  public uint WorldSize;

  /// <summary>
  /// Tribe chosen by the player creating the lobby
  /// </summary>
  [PacketField]
  public uint Tribe;
}

/// <summary>
/// Server response to <see cref="CreateLobbyPacket"/>
/// </summary>
[GeneratedPacket]
public partial class CreateLobbyResponsePacket : IPacket {
  /// <summary>
  /// Result of the operation
  /// </summary>
  [PacketField]
  public LobbyActionResult Result;

  /// <summary>
  /// Id of the newly created lobby
  /// </summary>
  /// <remarks>
  /// Valid only if <see cref="Result"/> is <see cref="LobbyActionResult.Ok"/>
  /// </remarks>
  [PacketField]
  public ulong LobbyId;
}

/// <summary>
/// Joins an existing lobby
/// </summary>
[GeneratedPacket]
public partial class JoinLobbyPacket : IPacket {
  /// <summary>
  /// Id of the lobby to join
  /// </summary>
  [PacketField]
  public ulong LobbyId;

  /// <summary>
  /// Tribe chosen by the player
  /// </summary>
  [PacketField]
  public uint Tribe;
}

/// <summary>
/// Server response to <see cref="JoinLobbyPacket"/>
/// </summary>
[GeneratedPacket]
public partial class JoinLobbyResponsePacket : IPacket {
  /// <summary>
  /// Result of the operation
  /// </summary>
  [PacketField]
  public LobbyActionResult Result;

  /// <summary>
  /// Id of the lobby the player tried to join
  /// </summary>
  [PacketField]
  public ulong LobbyId;
}

/// <summary>
/// Leaves a lobby the player joined before
/// </summary>
[GeneratedPacket]
public partial class LeaveLobbyPacket : IPacket {
  /// <summary>
  /// Id of the lobby to leave
  /// </summary>
  [PacketField]
  public ulong LobbyId;
}

/// <summary>
/// Server response to <see cref="LeaveLobbyPacket"/>
/// </summary>
[GeneratedPacket]
public partial class LeaveLobbyResponsePacket : IPacket {
  /// <summary>
  /// Result of the operation
  /// </summary>
  [PacketField]
  public LobbyActionResult Result;

  /// <summary>
  /// Id of the lobby the player tried to leave
  /// </summary>
  [PacketField]
  public ulong LobbyId;
}

/// <summary>
/// Updates the ready state of the player in a lobby
/// </summary>
/// <remarks>
/// When all the players in a lobby are ready, the game starts
/// </remarks>
[GeneratedPacket]
public partial class SetReadyPacket : IPacket {
  /// <summary>
  /// Id of the lobby
  /// </summary>
  [PacketField]
  public ulong LobbyId;

  /// <summary>
  /// true to mark the player as ready, false to remove the ready state
  /// </summary>
  [PacketField]
  public bool Ready;
}

/// <summary>
/// Server response to <see cref="SetReadyPacket"/>
/// </summary>
[GeneratedPacket]
public partial class SetReadyResponsePacket : IPacket {
  /// <summary>
  /// Result of the operation
  /// </summary>
  [PacketField]
  public LobbyActionResult Result;

  /// <summary>
  /// Id of the lobby
  /// </summary>
  [PacketField]
  public ulong LobbyId;
}

/// <summary>
/// Broadcast by the server when a lobby gets created or modified
/// </summary>
[GeneratedPacket]
public partial class LobbyUpdatedPacket : IPacket {
  /// <summary>
  /// The new state of the lobby
  /// </summary>
  [PacketField]
  public LobbyData Lobby { get; set; } = new();
}

/// <summary>
/// Broadcast by the server when a lobby gets deleted
/// </summary>
[GeneratedPacket]
public partial class LobbyDeletedPacket : IPacket {
  /// <summary>
  /// Id of the deleted lobby
  /// </summary>
  [PacketField]
  public ulong LobbyId;
}

/// <summary>
/// Sent by the server to every player of a lobby when its game starts
/// </summary>
[GeneratedPacket]
public partial class GameStartedPacket : IPacket {
  /// <summary>
  /// Id of the lobby whose game started
  /// </summary>
  [PacketField]
  public ulong LobbyId;

  /// <summary>
  /// The players taking part in the game
  /// </summary>
  [PacketField]
  public List<LobbyPlayerData> Players = [];
}
