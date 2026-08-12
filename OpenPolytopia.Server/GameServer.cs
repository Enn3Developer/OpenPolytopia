namespace OpenPolytopia.Server;

using OpenPolytopia.Common;
using OpenPolytopia.Common.Network;
using OpenPolytopia.Common.Network.Packets;

/// <summary>
/// The game server: manages players, lobbies and starting games on top of a <see cref="ServerConnection"/>
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
      // a failing handler must not go unnoticed nor take the server down
      Console.Error.WriteLine($"Error while managing {packet.GetType().Name} from client {connection.Id}: {e}");
    }
  }

  private async Task DispatchPacketAsync(NetworkConnection connection, IPacket packet) {
    switch (packet) {
      // handshake, respond with the result of the version check and the assigned player id
      case HandshakePacket handshake:
        await _server.SendToAsync(connection.Id,
          new HandshakeResponsePacket { Ok = handshake.Version == NetworkConstants.VERSION, PlayerId = connection.Id });
        break;
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
    GetLobbiesResponsePacket response;

    await _stateLock.WaitAsync();
    try {
      response = new GetLobbiesResponsePacket { Lobbies = [.. _lobbyManager.Lobbies] };
    }
    finally {
      _stateLock.Release();
    }

    await _server.SendToAsync(connection.Id, response);
  }

  private async Task ManageCreateLobbyAsync(NetworkConnection connection, CreateLobbyPacket packet) {
    LobbyData? lobby = null;
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
        lobby = _lobbyManager.CreateLobby(packet.MaxPlayers,
          new LobbyPlayerData { PlayerId = connection.Id, Name = name, Tribe = packet.Tribe });
      }
    }
    finally {
      _stateLock.Release();
    }

    await _server.SendToAsync(connection.Id,
      new CreateLobbyResponsePacket { Result = result, LobbyId = lobby?.Id ?? 0 });

    if (lobby != null) {
      await _server.BroadcastAsync(new LobbyUpdatedPacket { Lobby = lobby });
    }
  }

  private async Task ManageJoinLobbyAsync(NetworkConnection connection, JoinLobbyPacket packet) {
    LobbyData? lobby = null;
    LobbyActionResult result;

    await _stateLock.WaitAsync();
    try {
      if (!_playerNames.TryGetValue(connection.Id, out var name)) {
        result = LobbyActionResult.NotRegistered;
      }
      else {
        result = _lobbyManager.JoinLobby(packet.LobbyId,
          new LobbyPlayerData { PlayerId = connection.Id, Name = name, Tribe = packet.Tribe });
        lobby = _lobbyManager[packet.LobbyId];
      }
    }
    finally {
      _stateLock.Release();
    }

    await _server.SendToAsync(connection.Id, new JoinLobbyResponsePacket { Result = result, LobbyId = packet.LobbyId });

    if (result == LobbyActionResult.Ok && lobby != null) {
      await _server.BroadcastAsync(new LobbyUpdatedPacket { Lobby = lobby });
    }
  }

  private async Task ManageLeaveLobbyAsync(NetworkConnection connection, LeaveLobbyPacket packet) {
    LobbyData? lobby = null;
    var deleted = false;
    LobbyActionResult result;

    await _stateLock.WaitAsync();
    try {
      result = _lobbyManager.LeaveLobby(packet.LobbyId, connection.Id);

      if (result == LobbyActionResult.Ok) {
        lobby = _lobbyManager[packet.LobbyId];

        // remove the lobby if it became empty
        if (lobby is { PlayersCount: 0 }) {
          _lobbyManager.RemoveLobby(lobby.Id);
          deleted = true;
        }
      }
    }
    finally {
      _stateLock.Release();
    }

    await _server.SendToAsync(connection.Id,
      new LeaveLobbyResponsePacket { Result = result, LobbyId = packet.LobbyId });

    if (result != LobbyActionResult.Ok || lobby == null) {
      return;
    }

    if (deleted) {
      await _server.BroadcastAsync(new LobbyDeletedPacket { LobbyId = lobby.Id });
    }
    else {
      await _server.BroadcastAsync(new LobbyUpdatedPacket { Lobby = lobby });
    }
  }

  private async Task ManageSetReadyAsync(NetworkConnection connection, SetReadyPacket packet) {
    LobbyData? lobby = null;
    LobbyActionResult result;

    await _stateLock.WaitAsync();
    try {
      result = _lobbyManager.SetReady(packet.LobbyId, connection.Id, packet.Ready);
      if (result == LobbyActionResult.Ok) {
        lobby = _lobbyManager[packet.LobbyId];
      }
    }
    finally {
      _stateLock.Release();
    }

    await _server.SendToAsync(connection.Id, new SetReadyResponsePacket { Result = result, LobbyId = packet.LobbyId });

    if (result == LobbyActionResult.Ok && lobby != null) {
      await _server.BroadcastAsync(new LobbyUpdatedPacket { Lobby = lobby });
    }
  }

  private async Task ClientDisconnectedAsync(NetworkConnection connection) {
    List<LobbyData> updated = [];
    List<ulong> deletedIds = [];

    await _stateLock.WaitAsync();
    try {
      _playerNames.Remove(connection.Id);
      _lobbyManager.RemovePlayerFromAllLobbies(connection.Id, updated, deletedIds);
    }
    finally {
      _stateLock.Release();
    }

    foreach (var lobby in updated) {
      await _server.BroadcastAsync(new LobbyUpdatedPacket { Lobby = lobby });
    }

    foreach (var id in deletedIds) {
      await _server.BroadcastAsync(new LobbyDeletedPacket { LobbyId = id });
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
