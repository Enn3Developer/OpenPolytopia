namespace OpenPolytopia.Server;

using OpenPolytopia.Common;
using OpenPolytopia.Common.Network;
using OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Manages players, lobbies and starting games on top of a <see cref="ServerConnection"/>
/// </summary>
/// <param name="port">the port to listen on</param>
/// <param name="bindAddress">the ip address to bind to; null to listen on every interface</param>
public class GameServer(int port, string? bindAddress = null) : IDisposable {
  /// <summary>
  /// How often the server checks for lobbies to start
  /// </summary>
  private static readonly TimeSpan START_LOBBY_INTERVAL = TimeSpan.FromSeconds(5);

  private readonly ServerConnection _server = new(port, bindAddress);
  private readonly LobbyManager _lobbyManager = new();
  private readonly Dictionary<uint, string> _playerNames = new();
  private readonly HashSet<uint> _handshakeDone = [];

  // guards _lobbyManager, _playerNames and _handshakeDone: packet handlers run on many client tasks
  private readonly SemaphoreSlim _stateLock = new(1, 1);

  private readonly CancellationTokenSource _cts = new();

  /// <summary>
  /// Runs the server until <see cref="Stop"/> gets called
  /// </summary>
  public async Task RunAsync() {
    _server.OnPacketReceived += ManagePacketAsync;
    _server.OnClientDisconnected += connection => _ = ClientDisconnectedAsync(connection);

    // check for lobbies to start in background
    _ = StartLobbiesLoopAsync(_cts.Token);

    Console.WriteLine($"Server listening on {bindAddress ?? "*"}:{port}");
    await _server.RunAsync();
  }

  /// <summary>
  /// Stops the server
  /// </summary>
  public void Stop() {
    _cts.Cancel();
    _server.Stop();
  }

  private async Task ManagePacketAsync(NetworkConnection connection, IPacket packet) {
    try {
      await DispatchPacketAsync(connection, packet);
    }
    catch (Exception e) {
      // log the error without taking the server down
      Console.Error.WriteLine($"Error while managing {packet.GetType().Name} from client {connection.Id}: {e}");
    }
  }

  private async Task DispatchPacketAsync(NetworkConnection connection, IPacket packet) {
    if (packet is HandshakePacket handshake) {
      await ManageHandshakeAsync(connection, handshake);
      return;
    }

    // kick clients that send anything else before a successful handshake
    bool handshakeDone;
    await _stateLock.WaitAsync();
    try {
      handshakeDone = _handshakeDone.Contains(connection.Id);
    }
    finally {
      _stateLock.Release();
    }

    if (!handshakeDone) {
      connection.Close();
      return;
    }

    switch (packet) {
      // register the player or rename him
      case SetNamePacket setName:
        await ManageSetNameAsync(connection, setName);
        break;
      // respond with all the lobbies currently on the server
      case GetLobbiesPacket:
        await ManageGetLobbiesAsync(connection);
        break;
      // create a new lobby with the sender inside
      case CreateLobbyPacket createLobby:
        await ManageCreateLobbyAsync(connection, createLobby);
        break;
      // add the sender to an existing lobby
      case JoinLobbyPacket joinLobby:
        await ManageJoinLobbyAsync(connection, joinLobby);
        break;
      // remove the sender from a lobby
      case LeaveLobbyPacket leaveLobby:
        await ManageLeaveLobbyAsync(connection, leaveLobby);
        break;
      // update the ready state of the sender in a lobby
      case SetReadyPacket setReady:
        await ManageSetReadyAsync(connection, setReady);
        break;
    }
  }

  private async Task ManageHandshakeAsync(NetworkConnection connection, HandshakePacket packet) {
    var ok = packet.Version == NetworkConstants.VERSION;

    if (ok) {
      await _stateLock.WaitAsync();
      try {
        _handshakeDone.Add(connection.Id);
      }
      finally {
        _stateLock.Release();
      }
    }

    await _server.SendToAsync(connection.Id, new HandshakeResponsePacket { Ok = ok, PlayerId = connection.Id });

    // kick clients with an incompatible version
    if (!ok) {
      connection.Close();
    }
  }

  private async Task ManageSetNameAsync(NetworkConnection connection, SetNamePacket packet) {
    var name = packet.Name.Trim();
    var ok = name.Length is > 0 and <= 32;

    if (ok) {
      await _stateLock.WaitAsync();
      try {
        _playerNames[connection.Id] = name;
      }
      finally {
        _stateLock.Release();
      }
    }

    await _server.SendToAsync(connection.Id, new SetNameResponsePacket { Ok = ok });
  }

  private async Task ManageGetLobbiesAsync(NetworkConnection connection) {
    byte[] response;

    // frame the packet while holding the lock, the lobby data is shared
    await _stateLock.WaitAsync();
    try {
      response = PacketProtocol.FramePacket(new GetLobbiesResponsePacket { Lobbies = [.. _lobbyManager.Lobbies] });
    }
    finally {
      _stateLock.Release();
    }

    await _server.SendToAsync(connection.Id, response);
  }

  private async Task ManageCreateLobbyAsync(NetworkConnection connection, CreateLobbyPacket packet) {
    var lobbyId = 0ul;
    byte[]? lobbyUpdate = null;
    var result = LobbyActionResult.Ok;

    await _stateLock.WaitAsync();
    try {
      if (!_playerNames.TryGetValue(connection.Id, out var name)) {
        result = LobbyActionResult.NotRegistered;
      }
      else if (packet.MaxPlayers is < 2 or > 16) {
        result = LobbyActionResult.InvalidParameters;
      }
      else {
        var lobby = _lobbyManager.CreateLobby(packet.MaxPlayers,
          new LobbyPlayerData { PlayerId = connection.Id, Name = name, Tribe = packet.Tribe });
        lobbyId = lobby.Id;

        // frame the broadcast while holding the lock, the lobby data is shared
        lobbyUpdate = PacketProtocol.FramePacket(new LobbyUpdatedPacket { Lobby = lobby });
      }
    }
    finally {
      _stateLock.Release();
    }

    await _server.SendToAsync(connection.Id, new CreateLobbyResponsePacket { Result = result, LobbyId = lobbyId });

    if (lobbyUpdate != null) {
      await _server.BroadcastAsync(lobbyUpdate);
    }
  }

  private async Task ManageJoinLobbyAsync(NetworkConnection connection, JoinLobbyPacket packet) {
    byte[]? lobbyUpdate = null;
    LobbyActionResult result;

    await _stateLock.WaitAsync();
    try {
      if (!_playerNames.TryGetValue(connection.Id, out var name)) {
        result = LobbyActionResult.NotRegistered;
      }
      else {
        result = _lobbyManager.JoinLobby(packet.LobbyId,
          new LobbyPlayerData { PlayerId = connection.Id, Name = name, Tribe = packet.Tribe });
        var lobby = _lobbyManager[packet.LobbyId];

        if (result == LobbyActionResult.Ok && lobby != null) {
          // frame the broadcast while holding the lock, the lobby data is shared
          lobbyUpdate = PacketProtocol.FramePacket(new LobbyUpdatedPacket { Lobby = lobby });
        }
      }
    }
    finally {
      _stateLock.Release();
    }

    await _server.SendToAsync(connection.Id, new JoinLobbyResponsePacket { Result = result, LobbyId = packet.LobbyId });

    if (lobbyUpdate != null) {
      await _server.BroadcastAsync(lobbyUpdate);
    }
  }

  private async Task ManageLeaveLobbyAsync(NetworkConnection connection, LeaveLobbyPacket packet) {
    byte[]? broadcast = null;
    LobbyActionResult result;

    await _stateLock.WaitAsync();
    try {
      result = _lobbyManager.LeaveLobby(packet.LobbyId, connection.Id);

      if (result == LobbyActionResult.Ok && _lobbyManager[packet.LobbyId] is { } lobby) {
        // remove the lobby if it became empty; frame the broadcast while holding the lock
        if (lobby.PlayersCount == 0) {
          _lobbyManager.RemoveLobby(lobby.Id);
          broadcast = PacketProtocol.FramePacket(new LobbyDeletedPacket { LobbyId = lobby.Id });
        }
        else {
          broadcast = PacketProtocol.FramePacket(new LobbyUpdatedPacket { Lobby = lobby });
        }
      }
    }
    finally {
      _stateLock.Release();
    }

    await _server.SendToAsync(connection.Id,
      new LeaveLobbyResponsePacket { Result = result, LobbyId = packet.LobbyId });

    if (broadcast != null) {
      await _server.BroadcastAsync(broadcast);
    }
  }

  private async Task ManageSetReadyAsync(NetworkConnection connection, SetReadyPacket packet) {
    byte[]? lobbyUpdate = null;
    LobbyActionResult result;

    await _stateLock.WaitAsync();
    try {
      result = _lobbyManager.SetReady(packet.LobbyId, connection.Id, packet.Ready);

      if (result == LobbyActionResult.Ok && _lobbyManager[packet.LobbyId] is { } lobby) {
        // frame the broadcast while holding the lock, the lobby data is shared
        lobbyUpdate = PacketProtocol.FramePacket(new LobbyUpdatedPacket { Lobby = lobby });
      }
    }
    finally {
      _stateLock.Release();
    }

    await _server.SendToAsync(connection.Id, new SetReadyResponsePacket { Result = result, LobbyId = packet.LobbyId });

    if (lobbyUpdate != null) {
      await _server.BroadcastAsync(lobbyUpdate);
    }
  }

  private async Task ClientDisconnectedAsync(NetworkConnection connection) {
    List<byte[]> broadcasts = [];

    await _stateLock.WaitAsync();
    try {
      _handshakeDone.Remove(connection.Id);
      _playerNames.Remove(connection.Id);

      List<LobbyData> updated = [];
      List<ulong> deletedIds = [];
      _lobbyManager.RemovePlayerFromAllLobbies(connection.Id, updated, deletedIds);

      // frame the broadcasts while holding the lock, the lobby data is shared
      broadcasts.AddRange(updated.Select(lobby => PacketProtocol.FramePacket(new LobbyUpdatedPacket { Lobby = lobby })));
      broadcasts.AddRange(deletedIds.Select(id => PacketProtocol.FramePacket(new LobbyDeletedPacket { LobbyId = id })));
    }
    finally {
      _stateLock.Release();
    }

    foreach (var broadcast in broadcasts) {
      await _server.BroadcastAsync(broadcast);
    }
  }

  private async Task StartLobbiesLoopAsync(CancellationToken ct) {
    using var timer = new PeriodicTimer(START_LOBBY_INTERVAL);

    try {
      while (await timer.WaitForNextTickAsync(ct)) {
        List<LobbyData> starting;

        await _stateLock.WaitAsync(ct);
        try {
          starting = _lobbyManager.TakeStartingLobbies();
        }
        finally {
          _stateLock.Release();
        }

        foreach (var lobby in starting) {
          // TODO: world generation
          // TODO: initialize game data
          // TODO: add players to the game

          Console.WriteLine($"Starting game for lobby {lobby.Id} with {lobby.PlayersCount} players");

          // notify the players that their game started and remove the lobby from the list
          await _server.BroadcastToAsync(lobby.Players.Select(player => player.PlayerId),
            new GameStartedPacket { LobbyId = lobby.Id, Players = lobby.Players });
          await _server.BroadcastAsync(new LobbyDeletedPacket { LobbyId = lobby.Id });
        }
      }
    }
    catch (OperationCanceledException) {
      // server stopping
    }
  }

  public void Dispose() {
    Stop();
    _cts.Dispose();
    _server.Dispose();
    _stateLock.Dispose();
    GC.SuppressFinalize(this);
  }
}
