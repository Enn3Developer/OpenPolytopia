namespace OpenPolytopia.Server;

using OpenPolytopia.Common;
using OpenPolytopia.Common.Network;
using OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Manages players, lobbies and starting games on top of a <see cref="ServerConnection"/>
/// </summary>
/// <remarks>
/// Every packet is queued while holding the state lock,
/// so the clients receive the lobby updates in the same order the server applied them
/// </remarks>
/// <param name="port">the port to listen on</param>
/// <param name="bindAddress">the ip address to bind to; null to listen on every interface</param>
public class GameServer(int port, string? bindAddress = null) : IDisposable {
  /// <summary>
  /// How often the server checks for lobbies to start
  /// </summary>
  private static readonly TimeSpan START_LOBBY_INTERVAL = TimeSpan.FromSeconds(5);

  /// <summary>
  /// Max lobbies the server accepts before refusing new ones
  /// </summary>
  private const int MAX_LOBBIES = 100;

  private readonly ServerConnection _server = new(port, bindAddress);
  private readonly LobbyManager _lobbyManager = new();
  private readonly Dictionary<uint, string> _playerNames = new();

  // guards _lobbyManager and _playerNames: packet handlers run on many client tasks
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
      ManageHandshake(connection, handshake);
      return;
    }

    // kick clients that send anything else before a successful handshake
    if (!_server.IsHandshakeDone(connection.Id)) {
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

  private void ManageHandshake(NetworkConnection connection, HandshakePacket packet) {
    var ok = packet.Version == NetworkConstants.VERSION;

    if (ok) {
      _server.CompleteHandshake(connection.Id);
    }

    _server.SendTo(connection.Id, new HandshakeResponsePacket { Ok = ok, PlayerId = connection.Id });

    // kick clients with an incompatible version, after the response gets delivered
    if (!ok) {
      _server.Kick(connection.Id);
    }
  }

  private async Task ManageSetNameAsync(NetworkConnection connection, SetNamePacket packet) {
    var name = packet.Name.Trim();
    var ok = name.Length is > 0 and <= 32;

    await _stateLock.WaitAsync();
    try {
      if (ok) {
        _playerNames[connection.Id] = name;

        // propagate the rename into the lobbies the player joined
        List<LobbyData> updated = [];
        _lobbyManager.RenamePlayerInLobbies(connection.Id, name, updated);
        foreach (var lobby in updated) {
          _server.Broadcast(PacketProtocol.FramePacket(new LobbyUpdatedPacket { Lobby = lobby }));
        }
      }

      _server.SendTo(connection.Id, new SetNameResponsePacket { Ok = ok });
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageGetLobbiesAsync(NetworkConnection connection) {
    await _stateLock.WaitAsync();
    try {
      _server.SendTo(connection.Id, new GetLobbiesResponsePacket { Lobbies = [.. _lobbyManager.Lobbies] });
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageCreateLobbyAsync(NetworkConnection connection, CreateLobbyPacket packet) {
    await _stateLock.WaitAsync();
    try {
      LobbyData? lobby = null;
      LobbyActionResult result;

      if (!_playerNames.TryGetValue(connection.Id, out var name)) {
        result = LobbyActionResult.NotRegistered;
      }
      else if (packet.MaxPlayers is < 2 or > 16 || !Enum.IsDefined((TribeType)packet.Tribe)) {
        result = LobbyActionResult.InvalidParameters;
      }
      // one lobby per player and a global cap, or a client could flood the server
      else if (_lobbyManager.IsPlayerInAnyLobby(connection.Id)) {
        result = LobbyActionResult.AlreadyJoinedLobby;
      }
      else if (_lobbyManager.LobbiesCount >= MAX_LOBBIES) {
        result = LobbyActionResult.TooManyLobbies;
      }
      else {
        result = LobbyActionResult.Ok;
        lobby = _lobbyManager.CreateLobby(packet.MaxPlayers,
          new LobbyPlayerData { PlayerId = connection.Id, Name = name, Tribe = packet.Tribe });
      }

      _server.SendTo(connection.Id, new CreateLobbyResponsePacket { Result = result, LobbyId = lobby?.Id ?? 0 });

      if (lobby != null) {
        _server.Broadcast(PacketProtocol.FramePacket(new LobbyUpdatedPacket { Lobby = lobby }));
      }
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageJoinLobbyAsync(NetworkConnection connection, JoinLobbyPacket packet) {
    await _stateLock.WaitAsync();
    try {
      LobbyActionResult result;

      if (!_playerNames.TryGetValue(connection.Id, out var name)) {
        result = LobbyActionResult.NotRegistered;
      }
      else if (!Enum.IsDefined((TribeType)packet.Tribe)) {
        result = LobbyActionResult.InvalidParameters;
      }
      // one lobby per player, or a client could flood the server
      else if (_lobbyManager.IsPlayerInAnyLobby(connection.Id)) {
        result = LobbyActionResult.AlreadyJoinedLobby;
      }
      else {
        result = _lobbyManager.JoinLobby(packet.LobbyId,
          new LobbyPlayerData { PlayerId = connection.Id, Name = name, Tribe = packet.Tribe });
      }

      _server.SendTo(connection.Id, new JoinLobbyResponsePacket { Result = result, LobbyId = packet.LobbyId });

      if (result == LobbyActionResult.Ok && _lobbyManager[packet.LobbyId] is { } lobby) {
        _server.Broadcast(PacketProtocol.FramePacket(new LobbyUpdatedPacket { Lobby = lobby }));
      }
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageLeaveLobbyAsync(NetworkConnection connection, LeaveLobbyPacket packet) {
    await _stateLock.WaitAsync();
    try {
      var result = _lobbyManager.LeaveLobby(packet.LobbyId, connection.Id);

      _server.SendTo(connection.Id, new LeaveLobbyResponsePacket { Result = result, LobbyId = packet.LobbyId });

      if (result == LobbyActionResult.Ok && _lobbyManager[packet.LobbyId] is { } lobby) {
        // remove the lobby if it became empty
        if (lobby.PlayersCount == 0) {
          _lobbyManager.RemoveLobby(lobby.Id);
          _server.Broadcast(PacketProtocol.FramePacket(new LobbyDeletedPacket { LobbyId = lobby.Id }));
        }
        else {
          _server.Broadcast(PacketProtocol.FramePacket(new LobbyUpdatedPacket { Lobby = lobby }));
        }
      }
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageSetReadyAsync(NetworkConnection connection, SetReadyPacket packet) {
    await _stateLock.WaitAsync();
    try {
      var result = _lobbyManager.SetReady(packet.LobbyId, connection.Id, packet.Ready);

      _server.SendTo(connection.Id, new SetReadyResponsePacket { Result = result, LobbyId = packet.LobbyId });

      if (result == LobbyActionResult.Ok && _lobbyManager[packet.LobbyId] is { } lobby) {
        _server.Broadcast(PacketProtocol.FramePacket(new LobbyUpdatedPacket { Lobby = lobby }));
      }
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ClientDisconnectedAsync(NetworkConnection connection) {
    await _stateLock.WaitAsync();
    try {
      _playerNames.Remove(connection.Id);

      List<LobbyData> updated = [];
      List<ulong> deletedIds = [];
      _lobbyManager.RemovePlayerFromAllLobbies(connection.Id, updated, deletedIds);

      foreach (var lobby in updated) {
        _server.Broadcast(PacketProtocol.FramePacket(new LobbyUpdatedPacket { Lobby = lobby }));
      }

      foreach (var id in deletedIds) {
        _server.Broadcast(PacketProtocol.FramePacket(new LobbyDeletedPacket { LobbyId = id }));
      }
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task StartLobbiesLoopAsync(CancellationToken ct) {
    using var timer = new PeriodicTimer(START_LOBBY_INTERVAL);

    try {
      while (await timer.WaitForNextTickAsync(ct)) {
        await _stateLock.WaitAsync(ct);
        try {
          foreach (var lobby in _lobbyManager.TakeStartingLobbies()) {
            // TODO: world generation
            // TODO: initialize game data
            // TODO: add players to the game

            Console.WriteLine($"Starting game for lobby {lobby.Id} with {lobby.PlayersCount} players");

            // notify the players that their game started and remove the lobby from the list
            _server.BroadcastTo(lobby.Players.Select(player => player.PlayerId),
              new GameStartedPacket { LobbyId = lobby.Id, Players = lobby.Players });
            _server.Broadcast(new LobbyDeletedPacket { LobbyId = lobby.Id });
          }
        }
        finally {
          _stateLock.Release();
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
