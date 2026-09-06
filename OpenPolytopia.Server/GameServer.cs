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
public partial class GameServer(int port, string? bindAddress = null, string? databasePath = null) : IDisposable {
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
  private LobbyManager _lobbyManager = new();
  private GameManager _gameManager = new();
  private readonly Dictionary<uint, string> _playerNames = new();

  // loaded once: every game on this server shares the same static gameplay data
  private readonly GameData _gameData = GameData.LoadEmbedded();

  // guards _lobbyManager, _gameManager and _playerNames: packet handlers run on many client tasks
  private readonly SemaphoreSlim _stateLock = new(1, 1);

  private readonly PacketDispatcher<NetworkConnection> _dispatcher = new();

  private readonly CancellationTokenSource _cts = new();
  private int _activeRequests;

  /// <summary>
  /// Runs the server until <see cref="Stop"/> gets called
  /// </summary>
  public async Task RunAsync() {
    _server = new ServerConnection(port, bindAddress, _certificate);
    RestoreState(_store.LoadState());
    RegisterHandlers();

    _server.OnPacketReceived += ManagePacketAsync;
    _server.OnClientDisconnected += connection => _ = ClientDisconnectedAsync(connection);

    // check for lobbies to start in background
    var lobbyLoop = StartLobbiesLoopAsync(_cts.Token);

    Console.WriteLine($"Server listening on {bindAddress ?? "*"}:{port}");
    try { await _server.RunAsync(); }
    finally {
      _cts.Cancel();
      await lobbyLoop;
      while (Volatile.Read(ref _activeRequests) != 0) await Task.Delay(10);
      await _stateLock.WaitAsync();
      _stateLock.Release();
    }
  }

  /// <summary>
  /// Stops the server
  /// </summary>
  public void Stop() {
    _cts.Cancel();
    _server?.Stop();
  }

  private async Task ManagePacketAsync(NetworkConnection connection, IPacket packet) {
    if (_cts.IsCancellationRequested) return;
    Interlocked.Increment(ref _activeRequests);
    try {
      if (_server.IsHandshakeDone(connection.Id) &&
          packet is RegisterAccountPacket or LoginPacket or ResumeSessionPacket) {
        var authenticated = await CheckCredentialsAsync(connection, packet);
        await WithStateAsync(() => {
          Authenticate(connection, () => authenticated.Result, authenticated.Retryable);
          return Task.CompletedTask;
        }, false);
        return;
      }
      var mutates = packet is not HandshakePacket and not GetLobbiesPacket and not GetMyGamesPacket and
        not GetGameStatePacket and not JoinGamePacket and not LeaveGamePacket and not LogoutPacket;
      await WithStateAsync(() => DispatchPacketAsync(connection, packet), mutates);
    }
    catch (Exception e) {
      // log the error without taking the server down
      Console.Error.WriteLine($"Error while managing {packet.GetType().Name} from client {connection.Id}: {e}");
      connection.Close();
    }
    finally { Interlocked.Decrement(ref _activeRequests); }
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

    if (packet is not RegisterAccountPacket and not LoginPacket and not ResumeSessionPacket &&
        !_authenticated.ContainsKey(connection.Id)) {
      Kick(connection.Id);
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
    RegisterAccountHandlers();
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

    SendTo(connection.Id, new HandshakeResponsePacket { Ok = ok, PlayerId = 0 });

    // kick clients with an incompatible version, after the response gets delivered
    if (!ok) {
      Kick(connection.Id);
    }
  }

  private async Task ManageSetNameAsync(NetworkConnection connection, SetNamePacket packet) {
    var name = packet.Name.Trim();
    var ok = name.Length is > 0 and <= 32 && !name.Any(char.IsControl);

    {
      if (ok) {
        _pendingRenames[AccountId(connection)] = name;
        _playerNames[connection.Id] = name;
        foreach (var session in _gameManager.FindByAccount(AccountId(connection))) session.Rename(AccountId(connection), name);

        // propagate the rename into the lobbies the player joined
        List<LobbyData> updated = [];
        _lobbyManager.RenamePlayerInLobbies(AccountId(connection), name, updated);
        foreach (var lobby in updated) {
          Broadcast(new LobbyUpdatedPacket { Lobby = lobby });
        }
      }

      SendTo(connection.Id, new SetNameResponsePacket { Ok = ok });
    }
  }

  private async Task ManageGetLobbiesAsync(NetworkConnection connection) {
    {
      SendTo(connection.Id, new GetLobbiesResponsePacket { Lobbies = [.. _lobbyManager.Lobbies] });
    }
  }

  private async Task ManageCreateLobbyAsync(NetworkConnection connection, CreateLobbyPacket packet) {
    {
      LobbyData? lobby = null;
      LobbyActionResult result;

      if (!_playerNames.TryGetValue(connection.Id, out var name)) {
        result = LobbyActionResult.NotRegistered;
      }
      // only a registered tribe can be played: the game refuses to start with one it has no data for
      else if (_gameData.Tribes[(TribeType)packet.Tribe] == null) {
        result = LobbyActionResult.InvalidParameters;
      }
      else if (_lobbyManager.LobbiesCount >= MAX_LOBBIES) {
        result = LobbyActionResult.TooManyLobbies;
      }
      else {
        // the lobby rules themselves are checked by the manager, so a lobby is never half-valid
        result = _lobbyManager.CreateLobby(packet.MaxPlayers, packet.WorldSize,
          new LobbyPlayerData { PlayerId = AccountId(connection), Name = name, Tribe = packet.Tribe }, out lobby, packet.TimerMode);
      }

      SendTo(connection.Id, new CreateLobbyResponsePacket { Result = result, LobbyId = lobby?.Id ?? 0 });

      if (lobby != null) {
        Broadcast(new LobbyUpdatedPacket { Lobby = lobby });
      }
    }
  }

  private async Task ManageJoinLobbyAsync(NetworkConnection connection, JoinLobbyPacket packet) {
    {
      LobbyActionResult result;

      if (!_playerNames.TryGetValue(connection.Id, out var name)) {
        result = LobbyActionResult.NotRegistered;
      }
      // only a registered tribe can be played: the game refuses to start with one it has no data for
      else if (_gameData.Tribes[(TribeType)packet.Tribe] == null) {
        result = LobbyActionResult.InvalidParameters;
      }
      else {
        result = _lobbyManager.JoinLobby(packet.LobbyId,
          new LobbyPlayerData { PlayerId = AccountId(connection), Name = name, Tribe = packet.Tribe });
      }

      SendTo(connection.Id, new JoinLobbyResponsePacket { Result = result, LobbyId = packet.LobbyId });

      if (result == LobbyActionResult.Ok && _lobbyManager[packet.LobbyId] is { } lobby) {
        Broadcast(new LobbyUpdatedPacket { Lobby = lobby });
      }
    }
  }

  private async Task ManageLeaveLobbyAsync(NetworkConnection connection, LeaveLobbyPacket packet) {
    {
      var result = _lobbyManager.LeaveLobby(packet.LobbyId, AccountId(connection));

      SendTo(connection.Id, new LeaveLobbyResponsePacket { Result = result, LobbyId = packet.LobbyId });

      if (result == LobbyActionResult.Ok) {
        // the manager drops a lobby as soon as its last player leaves, so a missing
        // lobby here means it became empty
        if (_lobbyManager[packet.LobbyId] is { } lobby) {
          Broadcast(new LobbyUpdatedPacket { Lobby = lobby });
        }
        else {
          Broadcast(new LobbyDeletedPacket { LobbyId = packet.LobbyId });
        }
      }
    }
  }

  private async Task ManageSetReadyAsync(NetworkConnection connection, SetReadyPacket packet) {
    {
      var result = _lobbyManager.SetReady(packet.LobbyId, AccountId(connection), packet.Ready);

      SendTo(connection.Id, new SetReadyResponsePacket { Result = result, LobbyId = packet.LobbyId });

      if (result == LobbyActionResult.Ok && _lobbyManager[packet.LobbyId] is { } lobby) {
        Broadcast(new LobbyUpdatedPacket { Lobby = lobby });
      }
    }
  }

  private Task ClientDisconnectedAsync(NetworkConnection connection) => _cts.IsCancellationRequested ? Task.CompletedTask : WithStateAsync(() => {
    _playerNames.Remove(connection.Id);
    _authenticated.Remove(connection.Id);
    _sessionTokens.Remove(connection.Id);
    _gameManager.Disconnect(connection.Id);
    return Task.CompletedTask;
  }, false);

  /// <summary>
  /// Tells the players of a finished game who won and forgets the game
  /// </summary>
  /// <param name="session">the session whose game is over</param>
  private void EndGame(GameSession session) {
    BroadcastTo(session.ConnectionIds, new GameOverPacket {
      GameId = session.Id, Winner = (uint)session.Game.Winner, Players = session.TakeUpdate().Players
    });
    // Retain completed games so their members can reconnect and inspect the result.
  }

  private async Task StartLobbiesLoopAsync(CancellationToken ct) {
    using var timer = new PeriodicTimer(START_LOBBY_INTERVAL);

    try {
      while (await timer.WaitForNextTickAsync(ct)) {
        try {
          await WithStateAsync(async () => {
            foreach (var lobby in _lobbyManager.TakeStartingLobbies()) await StartLobbyAsync(lobby);
          }, false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
          Console.Error.WriteLine($"Lobby update failed; state restored: {e}");
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
    var online = _authenticated.ToDictionary(pair => pair.Value, pair => pair.Key);
    var connectionIds = lobby.Players.Where(player => online.ContainsKey(player.PlayerId))
      .Select(player => online[player.PlayerId]).ToList();

    try {
      var session = await _gameManager.CreateGameAsync(lobby, _gameData, onlineConnections: online);

      Console.WriteLine($"Starting game for lobby {lobby.Id} with {lobby.PlayersCount} players");

      // notify the players that their game started and send them its full state
      BroadcastTo(connectionIds, new GameStartedPacket { LobbyId = lobby.Id, Players = lobby.Players });
      BroadcastTo(connectionIds, session.BuildState());
    }
    catch (Exception e) {
      Console.Error.WriteLine($"Couldn't start the game for lobby {lobby.Id}: {e}");
      throw;
    }

    Broadcast(new LobbyDeletedPacket { LobbyId = lobby.Id });
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
    session = FindSession(gameId, AccountId(connection));
    if (session?.Game.Over == true) session.Join(AccountId(connection), connection.Id);
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
    {
      var response = TryResolveSession(connection, packet.GameId, out var session, out _, out var check)
        ? session.BuildState()
        : new GameStatePacket { Result = check, GameId = packet.GameId };

      SendTo(connection.Id, response);
    }
  }

  private async Task ManageMoveTroopAsync(NetworkConnection connection, MoveTroopPacket packet) {
    {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        SendTo(connection.Id, new MoveTroopResponsePacket { Result = check });
        return;
      }

      var result = session.Game.MoveTroop(playerId, packet.From, packet.To);
      SendTo(connection.Id, new MoveTroopResponsePacket { Result = result });

      if (result != GameActionResult.Ok) {
        return;
      }

      BroadcastTo(session.ConnectionIds, new TroopMovedPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, From = packet.From, To = packet.To,
        Update = session.TakeUpdate()
      });
    }
  }

  private async Task ManageAttackAsync(NetworkConnection connection, AttackPacket packet) {
    {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        SendTo(connection.Id, new AttackResponsePacket { Result = check });
        return;
      }

      var result = session.Game.Attack(playerId, packet.From, packet.Target);
      SendTo(connection.Id, new AttackResponsePacket { Result = result.Result });

      if (result.Result != GameActionResult.Ok) {
        return;
      }

      BroadcastTo(session.ConnectionIds, new CombatPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, From = packet.From, Target = packet.Target,
        AttackerHp = result.AttackerHp, DefenderHp = result.DefenderHp, AttackerKilled = result.AttackerKilled,
        DefenderKilled = result.DefenderKilled, AttackerPosition = result.AttackerPosition,
        Update = session.TakeUpdate()
      });
    }
  }

  private async Task ManageTrainTroopAsync(NetworkConnection connection, TrainTroopPacket packet) {
    {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        SendTo(connection.Id, new TrainTroopResponsePacket { Result = check });
        return;
      }

      var result = session.Game.TrainTroop(playerId, packet.City, (TroopType)packet.TroopType);
      SendTo(connection.Id, new TrainTroopResponsePacket { Result = result });

      if (result != GameActionResult.Ok) {
        return;
      }

      BroadcastTo(session.ConnectionIds, new TroopTrainedPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, Position = packet.City, TroopType = packet.TroopType,
        Update = session.TakeUpdate()
      });
    }
  }

  private async Task ManageResearchTechAsync(NetworkConnection connection, ResearchTechPacket packet) {
    {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        SendTo(connection.Id, new ResearchTechResponsePacket { Result = check });
        return;
      }

      var result = session.Game.ResearchTech(playerId, packet.TechId);
      SendTo(connection.Id, new ResearchTechResponsePacket { Result = result });

      if (result != GameActionResult.Ok) {
        return;
      }

      BroadcastTo(session.ConnectionIds, new TechResearchedPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, TechId = packet.TechId, Update = session.TakeUpdate()
      });
    }
  }

  private async Task ManageBuildAsync(NetworkConnection connection, BuildPacket packet) {
    {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        SendTo(connection.Id, new BuildResponsePacket { Result = check });
        return;
      }

      var result = session.Game.Build(playerId, packet.Position, (BuildingType)packet.Building);
      SendTo(connection.Id, new BuildResponsePacket { Result = result.Result });

      if (result.Result != GameActionResult.Ok) {
        return;
      }

      BroadcastTo(session.ConnectionIds, new BuildingBuiltPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, Position = packet.Position, Building = packet.Building,
        Update = session.TakeUpdate()
      });
    }
  }

  private async Task ManageCaptureAsync(NetworkConnection connection, CapturePacket packet) {
    {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        SendTo(connection.Id, new CaptureResponsePacket { Result = check });
        return;
      }

      var result = session.Game.Capture(playerId, packet.Position);
      SendTo(connection.Id, new CaptureResponsePacket { Result = result.Result });

      if (result.Result != GameActionResult.Ok) {
        return;
      }

      BroadcastTo(session.ConnectionIds, new CityCapturedPacket {
        GameId = packet.GameId, PlayerId = (uint)playerId, Position = packet.Position,
        PreviousOwner = (uint)result.PreviousOwner, EliminatedPlayer = (uint)result.EliminatedPlayer,
        Update = session.TakeUpdate()
      });

      // a capture can only end the game by eliminating the previous owner
      if (result.EliminatedPlayer != 0 && session.Game.Over) {
        EndGame(session);
      }
    }
  }

  private async Task ManageEndTurnAsync(NetworkConnection connection, EndTurnPacket packet) {
    {
      if (!TryResolveSession(connection, packet.GameId, out var session, out var playerId, out var check)) {
        SendTo(connection.Id, new EndTurnResponsePacket { Result = check });
        return;
      }

      var result = session.Game.EndTurn(playerId);
      SendTo(connection.Id, new EndTurnResponsePacket { Result = result.Result });

      if (result.Result != GameActionResult.Ok) {
        return;
      }

      if (result.GameOver) {
        EndGame(session);
      }
      else {
        BroadcastTo(session.ConnectionIds, new TurnStartedPacket {
          GameId = packet.GameId, Turn = result.Turn, PlayerId = (uint)result.NextPlayer,
          Update = session.TakeUpdate()
        });
      }
    }
  }

  public void Dispose() {
    Stop();
    _cts.Dispose();
    _server?.Dispose();
    _certificate?.Dispose();
    _store.Dispose();
    _stateLock.Dispose();
    GC.SuppressFinalize(this);
  }
}
