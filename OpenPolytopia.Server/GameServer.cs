namespace OpenPolytopia.Server;

using System.Diagnostics.CodeAnalysis;
using OpenPolytopia.Common;
using OpenPolytopia.Common.Gameplay;
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

  private readonly System.Security.Cryptography.X509Certificates.X509Certificate2? _certificate = ServerTls.LoadAndValidate(bindAddress);
  private ServerConnection _server = null!;
  private readonly LobbyManager _lobbyManager = new();
  private readonly GameManager _gameManager = new();
  private readonly Dictionary<uint, string> _playerNames = new();

  // loaded once: every game on this server shares the same static gameplay data
  private readonly GameData _gameData = GameData.LoadEmbedded();

  // guards _lobbyManager, _gameManager and _playerNames: packet handlers run on many client tasks
  private readonly SemaphoreSlim _stateLock = new(1, 1);

  private readonly PacketDispatcher<NetworkConnection> _dispatcher = new();

  private readonly CancellationTokenSource _cts = new();

  /// <summary>
  /// Runs the server until <see cref="Stop"/> gets called
  /// </summary>
  public async Task RunAsync() {
    _server = new ServerConnection(port, bindAddress, _certificate);
    RegisterHandlers();

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
    _server?.Stop();
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

    if (!await _dispatcher.DispatchAsync(connection, packet)) {
      // a client sending a packet the server never handles, e.g. a response packet
      Console.Error.WriteLine($"Unhandled {packet.GetType().Name} from client {connection.Id}");
    }
  }

  /// <summary>
  /// Registers a handler for every packet the server accepts
  /// </summary>
  /// <remarks>
  /// <see cref="HandshakePacket"/> isn't registered here: it has to run before the handshake gate
  /// in <see cref="DispatchPacketAsync"/>
  /// </remarks>
  private void RegisterHandlers() {
    // register the player or rename him
    _dispatcher.Register<SetNamePacket>(ManageSetNameAsync);
    // respond with all the lobbies currently on the server
    _dispatcher.Register<GetLobbiesPacket>((connection, _) => ManageGetLobbiesAsync(connection));
    // create a new lobby with the sender inside
    _dispatcher.Register<CreateLobbyPacket>(ManageCreateLobbyAsync);
    // add the sender to an existing lobby
    _dispatcher.Register<JoinLobbyPacket>(ManageJoinLobbyAsync);
    // remove the sender from a lobby
    _dispatcher.Register<LeaveLobbyPacket>(ManageLeaveLobbyAsync);
    // update the ready state of the sender in a lobby
    _dispatcher.Register<SetReadyPacket>(ManageSetReadyAsync);
    // respond with the full state of a game
    _dispatcher.Register<GetGameStatePacket>(ManageGetGameStateAsync);
    // move a troop of the sender's game
    _dispatcher.Register<MoveTroopPacket>(ManageMoveTroopAsync);
    // attack with a troop of the sender's game
    _dispatcher.Register<AttackPacket>(ManageAttackAsync);
    // train a troop in one of the sender's cities
    _dispatcher.Register<TrainTroopPacket>(ManageTrainTroopAsync);
    // research a tech node for the sender
    _dispatcher.Register<ResearchTechPacket>(ManageResearchTechAsync);
    // build on, or harvest, a tile of the sender's game
    _dispatcher.Register<BuildPacket>(ManageBuildAsync);
    // capture the city on a tile of the sender's game
    _dispatcher.Register<CapturePacket>(ManageCaptureAsync);
    // end the sender's turn
    _dispatcher.Register<EndTurnPacket>(ManageEndTurnAsync);
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
          _server.Broadcast(new LobbyUpdatedPacket { Lobby = lobby });
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
      // only a registered tribe can be played: the game refuses to start with one it has no data for
      else if (_gameData.Tribes[(TribeType)packet.Tribe] == null) {
        result = LobbyActionResult.InvalidParameters;
      }
      // a player can wait in one lobby or play one game, never both
      else if (_lobbyManager.IsPlayerInAnyLobby(connection.Id) ||
               _gameManager.FindByConnection(connection.Id) != null) {
        result = LobbyActionResult.AlreadyJoinedLobby;
      }
      else if (_lobbyManager.LobbiesCount >= MAX_LOBBIES) {
        result = LobbyActionResult.TooManyLobbies;
      }
      else {
        // the lobby rules themselves are checked by the manager, so a lobby is never half-valid
        result = _lobbyManager.CreateLobby(packet.MaxPlayers, packet.WorldSize,
          new LobbyPlayerData { PlayerId = connection.Id, Name = name, Tribe = packet.Tribe }, out lobby);
      }

      _server.SendTo(connection.Id, new CreateLobbyResponsePacket { Result = result, LobbyId = lobby?.Id ?? 0 });

      if (lobby != null) {
        _server.Broadcast(new LobbyUpdatedPacket { Lobby = lobby });
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
      // only a registered tribe can be played: the game refuses to start with one it has no data for
      else if (_gameData.Tribes[(TribeType)packet.Tribe] == null) {
        result = LobbyActionResult.InvalidParameters;
      }
      // a player can wait in one lobby or play one game, never both
      else if (_lobbyManager.IsPlayerInAnyLobby(connection.Id) ||
               _gameManager.FindByConnection(connection.Id) != null) {
        result = LobbyActionResult.AlreadyJoinedLobby;
      }
      else {
        result = _lobbyManager.JoinLobby(packet.LobbyId,
          new LobbyPlayerData { PlayerId = connection.Id, Name = name, Tribe = packet.Tribe });
      }

      _server.SendTo(connection.Id, new JoinLobbyResponsePacket { Result = result, LobbyId = packet.LobbyId });

      if (result == LobbyActionResult.Ok && _lobbyManager[packet.LobbyId] is { } lobby) {
        _server.Broadcast(new LobbyUpdatedPacket { Lobby = lobby });
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

      if (result == LobbyActionResult.Ok) {
        // the manager drops a lobby as soon as its last player leaves, so a missing
        // lobby here means it became empty
        if (_lobbyManager[packet.LobbyId] is { } lobby) {
          _server.Broadcast(new LobbyUpdatedPacket { Lobby = lobby });
        }
        else {
          _server.Broadcast(new LobbyDeletedPacket { LobbyId = packet.LobbyId });
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
        _server.Broadcast(new LobbyUpdatedPacket { Lobby = lobby });
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
        _server.Broadcast(new LobbyUpdatedPacket { Lobby = lobby });
      }

      foreach (var id in deletedIds) {
        _server.Broadcast(new LobbyDeletedPacket { LobbyId = id });
      }

      DisconnectFromGame(connection);
    }
    finally {
      _stateLock.Release();
    }
  }

  /// <summary>
  /// Resigns a disconnected player from the game they were playing, if any
  /// </summary>
  /// <remarks>
  /// The connection is forgotten first, so the packets that follow only reach the players still connected
  /// </remarks>
  /// <param name="connection">the connection that just disconnected</param>
  private void DisconnectFromGame(NetworkConnection connection) {
    var session = _gameManager.RemovePlayer(connection.Id, out var playerId);
    if (session == null || playerId == 0 || session.Game.Over) {
      return;
    }

    // resigning only passes the turn when it was the resigning player's turn; otherwise nothing changes for the others
    var heldTheTurn = session.Game.CurrentPlayer == playerId;
    var result = session.Game.Resign(playerId);
    if (result.Result != GameActionResult.Ok) {
      return;
    }

    _server.BroadcastTo(session.ConnectionIds,
      new PlayerEliminatedPacket { GameId = session.Id, PlayerId = (uint)playerId, Update = session.TakeUpdate() });

    if (result.GameOver) {
      EndGame(session);
    }
    else if (heldTheTurn) {
      _server.BroadcastTo(session.ConnectionIds, new TurnStartedPacket {
        GameId = session.Id, Turn = result.Turn, PlayerId = (uint)result.NextPlayer, Update = session.TakeUpdate()
      });
    }
  }

  /// <summary>
  /// Tells the players of a finished game who won and forgets the game
  /// </summary>
  /// <param name="session">the session whose game is over</param>
  private void EndGame(GameSession session) {
    _server.BroadcastTo(session.ConnectionIds, new GameOverPacket {
      GameId = session.Id, Winner = (uint)session.Game.Winner, Players = session.TakeUpdate().Players
    });
    _gameManager.RemoveGame(session.Id);
  }

  private async Task StartLobbiesLoopAsync(CancellationToken ct) {
    using var timer = new PeriodicTimer(START_LOBBY_INTERVAL);

    try {
      while (await timer.WaitForNextTickAsync(ct)) {
        await _stateLock.WaitAsync(ct);
        try {
          foreach (var lobby in _lobbyManager.TakeStartingLobbies()) {
            await StartLobbyAsync(lobby);
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

  /// <summary>
  /// Turns a lobby whose players are all ready into a running game
  /// </summary>
  /// <remarks>
  /// The lobby is already gone from the manager by now, so a failure here still tells every client the lobby is
  /// deleted instead of leaving them waiting on it, and never takes the start loop down with it
  /// </remarks>
  /// <param name="lobby">the lobby to start</param>
  private async Task StartLobbyAsync(LobbyData lobby) {
    var connectionIds = lobby.Players.Select(player => player.PlayerId).ToList();

    try {
      var session = await _gameManager.CreateGameAsync(lobby, _gameData);

      Console.WriteLine($"Starting game for lobby {lobby.Id} with {lobby.PlayersCount} players");

      // notify the players that their game started and send them its full state
      _server.BroadcastTo(connectionIds, new GameStartedPacket { LobbyId = lobby.Id, Players = lobby.Players });
      _server.BroadcastTo(connectionIds, session.BuildState());
    }
    catch (Exception e) {
      Console.Error.WriteLine($"Couldn't start the game for lobby {lobby.Id}: {e}");
    }

    _server.Broadcast(new LobbyDeletedPacket { LobbyId = lobby.Id });
  }

  /// <summary>
  /// Resolves the game session and player id a game packet applies to
  /// </summary>
  /// <remarks>
  /// Factors the lookup every game packet handler needs before it can call into the engine: the session for
  /// <see cref="OpenPolytopia.Common.Network.Packets.GetGameStatePacket.GameId"/>-like fields, and the id of the
  /// player <paramref name="connection"/> maps to inside it
  /// </remarks>
  /// <param name="connection">the connection that sent the packet</param>
  /// <param name="gameId">the id of the game the packet targets</param>
  /// <param name="session">the resolved session; non-null when this returns true</param>
  /// <param name="playerId">the id of the player <paramref name="connection"/> is in the game; 0 when this returns false</param>
  /// <param name="result">
  /// <see cref="GameActionResult.GameNotFound"/> or <see cref="GameActionResult.NotInGame"/> when this returns
  /// false; meaningless otherwise
  /// </param>
  /// <returns>true if a session was found and the connection is a player in it</returns>
  private bool TryResolveSession(NetworkConnection connection, ulong gameId,
    [NotNullWhen(true)] out GameSession? session, out int playerId, out GameActionResult result) {
    session = _gameManager[gameId];
    if (session == null) {
      playerId = 0;
      result = GameActionResult.GameNotFound;
      return false;
    }

    playerId = session.PlayerIdOf(connection.Id);
    if (playerId == 0) {
      result = GameActionResult.NotInGame;
      return false;
    }

    result = GameActionResult.Ok;
    return true;
  }

  private async Task ManageGetGameStateAsync(NetworkConnection connection, GetGameStatePacket packet) {
    await _stateLock.WaitAsync();
    try {
      var response = TryResolveSession(connection, packet.GameId, out var session, out _, out var check)
        ? session.BuildState()
        : new GameStatePacket { Result = check, GameId = packet.GameId };

      _server.SendTo(connection.Id, response);
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageMoveTroopAsync(NetworkConnection connection, MoveTroopPacket packet) {
    await _stateLock.WaitAsync();
    try {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        _server.SendTo(connection.Id, new MoveTroopResponsePacket { Result = check });
        return;
      }

      var result = session.Game.MoveTroop(playerId, packet.From, packet.To);
      _server.SendTo(connection.Id, new MoveTroopResponsePacket { Result = result });

      if (result != GameActionResult.Ok) {
        return;
      }

      _server.BroadcastTo(session.ConnectionIds, new TroopMovedPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, From = packet.From, To = packet.To,
        Update = session.TakeUpdate()
      });
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageAttackAsync(NetworkConnection connection, AttackPacket packet) {
    await _stateLock.WaitAsync();
    try {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        _server.SendTo(connection.Id, new AttackResponsePacket { Result = check });
        return;
      }

      var result = session.Game.Attack(playerId, packet.From, packet.Target);
      _server.SendTo(connection.Id, new AttackResponsePacket { Result = result.Result });

      if (result.Result != GameActionResult.Ok) {
        return;
      }

      _server.BroadcastTo(session.ConnectionIds, new CombatPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, From = packet.From, Target = packet.Target,
        AttackerHp = result.AttackerHp, DefenderHp = result.DefenderHp, AttackerKilled = result.AttackerKilled,
        DefenderKilled = result.DefenderKilled, AttackerPosition = result.AttackerPosition,
        Update = session.TakeUpdate()
      });
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageTrainTroopAsync(NetworkConnection connection, TrainTroopPacket packet) {
    await _stateLock.WaitAsync();
    try {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        _server.SendTo(connection.Id, new TrainTroopResponsePacket { Result = check });
        return;
      }

      var result = session.Game.TrainTroop(playerId, packet.City, (TroopType)packet.TroopType);
      _server.SendTo(connection.Id, new TrainTroopResponsePacket { Result = result });

      if (result != GameActionResult.Ok) {
        return;
      }

      _server.BroadcastTo(session.ConnectionIds, new TroopTrainedPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, Position = packet.City, TroopType = packet.TroopType,
        Update = session.TakeUpdate()
      });
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageResearchTechAsync(NetworkConnection connection, ResearchTechPacket packet) {
    await _stateLock.WaitAsync();
    try {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        _server.SendTo(connection.Id, new ResearchTechResponsePacket { Result = check });
        return;
      }

      var result = session.Game.ResearchTech(playerId, packet.TechId);
      _server.SendTo(connection.Id, new ResearchTechResponsePacket { Result = result });

      if (result != GameActionResult.Ok) {
        return;
      }

      _server.BroadcastTo(session.ConnectionIds, new TechResearchedPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, TechId = packet.TechId, Update = session.TakeUpdate()
      });
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageBuildAsync(NetworkConnection connection, BuildPacket packet) {
    await _stateLock.WaitAsync();
    try {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        _server.SendTo(connection.Id, new BuildResponsePacket { Result = check });
        return;
      }

      var result = session.Game.Build(playerId, packet.Position, (BuildingType)packet.Building);
      _server.SendTo(connection.Id, new BuildResponsePacket { Result = result.Result });

      if (result.Result != GameActionResult.Ok) {
        return;
      }

      _server.BroadcastTo(session.ConnectionIds, new BuildingBuiltPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, Position = packet.Position, Building = packet.Building,
        Update = session.TakeUpdate()
      });
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageCaptureAsync(NetworkConnection connection, CapturePacket packet) {
    await _stateLock.WaitAsync();
    try {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        _server.SendTo(connection.Id, new CaptureResponsePacket { Result = check });
        return;
      }

      var result = session.Game.Capture(playerId, packet.Position);
      _server.SendTo(connection.Id, new CaptureResponsePacket { Result = result.Result });

      if (result.Result != GameActionResult.Ok) {
        return;
      }

      _server.BroadcastTo(session.ConnectionIds, new CityCapturedPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, Position = packet.Position,
        PreviousOwner = (uint)result.PreviousOwner, EliminatedPlayer = (uint)result.EliminatedPlayer,
        Update = session.TakeUpdate()
      });

      // a capture can only end the game by eliminating the previous owner
      if (result.EliminatedPlayer != 0 && session.Game.Over) {
        EndGame(session);
      }
    }
    finally {
      _stateLock.Release();
    }
  }

  private async Task ManageEndTurnAsync(NetworkConnection connection, EndTurnPacket packet) {
    await _stateLock.WaitAsync();
    try {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        _server.SendTo(connection.Id, new EndTurnResponsePacket { Result = check });
        return;
      }

      var result = session.Game.EndTurn(playerId);
      _server.SendTo(connection.Id, new EndTurnResponsePacket { Result = result.Result });

      if (result.Result != GameActionResult.Ok) {
        return;
      }

      if (result.GameOver) {
        EndGame(session);
      }
      else {
        _server.BroadcastTo(session.ConnectionIds, new TurnStartedPacket {
          GameId = packet.GameId, Turn = result.Turn, PlayerId = (uint)result.NextPlayer,
          Update = session.TakeUpdate()
        });
      }
    }
    finally {
      _stateLock.Release();
    }
  }

  public void Dispose() {
    Stop();
    _cts.Dispose();
    _server?.Dispose();
    _certificate?.Dispose();
    _stateLock.Dispose();
    GC.SuppressFinalize(this);
  }
}
