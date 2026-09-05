namespace OpenPolytopia;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Common.Network;
using Common.Network.Packets;
using Godot;

/// <summary>
/// Manages the connection with the game server
/// </summary>
/// <remarks>
/// Add this node to every scene that talks to the server.
/// The underlying connection is shared between all the instances and survives scene changes:
/// the first node to enter the tree connects and the following ones reuse the same connection.
/// Packets are processed on the main thread in <see cref="_PhysicsProcess"/>,
/// so every event is safe to use with Godot nodes
/// </remarks>
[GlobalClass]
public partial class NetworkNode : Node {
  /// <summary>
  /// The instance currently processing the packets
  /// </summary>
  /// <remarks>
  /// It is the last instance that entered the tree,
  /// or null when no instance is inside the tree.
  /// Nodes that use it across scene changes should grab it once in <c>_Ready</c>
  /// </remarks>
  public static NetworkNode? Instance { get; private set; }

  /// <summary>
  /// Minimum length of an account username
  /// </summary>
  public const int MIN_USERNAME_LENGTH = 3;

  /// <summary>
  /// Maximum length of an account username
  /// </summary>
  public const int MAX_USERNAME_LENGTH = 32;

  /// <summary>
  /// Minimum length of an account password
  /// </summary>
  public const int MIN_PASSWORD_LENGTH = 12;

  /// <summary>
  /// Maximum length of an account password
  /// </summary>
  public const int MAX_PASSWORD_LENGTH = 128;

  private static readonly TimeSpan RECONNECT_DELAY = TimeSpan.FromSeconds(3);

  private const string HOST_ENV = "OPENPOLYTOPIA_SERVER_HOST";
  private const string PORT_ENV = "OPENPOLYTOPIA_SERVER_PORT";
  private const string HOST_ARG = "--server-host";
  private const string PORT_ARG = "--server-port";

  // the session tokens are credentials, they never leave the user data directory
  private const string SESSION_FILE = "user://sessions.cfg";
  private const string TOKEN_KEY = "token";

  // shared between all the instances so the connection survives scene changes
  private static ClientConnection? _connection;
  private static bool _handshakeDone;
  private static uint _connectionId;
  private static readonly List<LobbyData> _lobbies = [];
  private static readonly List<ulong> _myGames = [];
  private static int _disconnectedFlag;
  private static DateTime _reconnectAt = DateTime.MaxValue;
  private static string? _requestedName;

  // account state; it survives a reconnection so the session can be resumed automatically
  private static bool _authenticated;
  private static bool _loggingOut;
  private static bool _authInFlight;
  private static ulong _resumeAt = ulong.MaxValue;
  private static bool _retryable;

  /// <summary>Whether the last authentication response was a temporary refusal.</summary>
  public bool AuthenticationRetryable => _retryable;
  private static uint _accountId;
  private static string _accountName = "";
  private static string? _sessionToken;
  private static string _serverKey = "";
  private static IPacket? _pendingAuth;
  private static bool _resuming;

  private readonly PacketDispatcher<NetworkNode> _dispatcher = new();

  /// <summary>
  /// Host to connect to
  /// </summary>
  /// <remarks>
  /// Overridable with the <c>--server-host=</c> command line argument
  /// or the <c>OPENPOLYTOPIA_SERVER_HOST</c> environment variable
  /// </remarks>
  [Export]
  public string Host { get; set; } = "enn3.ovh";

  /// <summary>
  /// Port to connect to
  /// </summary>
  /// <remarks>
  /// Overridable with the <c>--server-port=</c> command line argument
  /// or the <c>OPENPOLYTOPIA_SERVER_PORT</c> environment variable
  /// </remarks>
  [Export(PropertyHint.Range, "1,65535")]
  public int Port { get; set; } = NetworkConstants.DEFAULT_PORT;

  /// <summary>
  /// If true, the node connects to the server as soon as it enters the tree
  /// and retries every <see cref="RECONNECT_DELAY"/> after a connection loss
  /// </summary>
  [Export]
  public bool AutoConnect { get; set; } = true;

  /// <summary>
  /// Id of this transport, assigned by the server during the handshake
  /// </summary>
  /// <remarks>
  /// It changes on every reconnection; use <see cref="PlayerId"/> to identify the player
  /// </remarks>
  public uint ConnectionId => _connectionId;

  /// <summary>
  /// Persistent id of the account this client is logged in as
  /// </summary>
  /// <remarks>
  /// Valid after <see cref="OnAuthenticated"/> gets fired with true; it's the id lobbies and games use
  /// </remarks>
  public uint PlayerId => _accountId;

  /// <summary>
  /// Display name of the account this client is logged in as
  /// </summary>
  public string AccountName => _accountName;

  /// <summary>
  /// true after a successful handshake with the server
  /// </summary>
  public bool Connected => _handshakeDone;

  /// <summary>
  /// true while the client is logged in on the server
  /// </summary>
  /// <remarks>
  /// Every request other than the account ones is dropped until this is true
  /// </remarks>
  public bool Authenticated => _authenticated;

  /// <summary>
  /// Lobbies data; it updates when the server broadcasts lobby changes
  /// </summary>
  public IReadOnlyList<LobbyData> Lobbies => _lobbies;

  /// <summary>
  /// Ids of the games this account takes part in; refreshed by <see cref="RefreshMyGames"/>
  /// </summary>
  public IReadOnlyList<ulong> MyGames => _myGames;

  /// <summary>
  /// Fired once after every change to <see cref="Lobbies"/>
  /// </summary>
  public event Action? OnLobbiesChanged;

  /// <summary>
  /// Fired once after every change to <see cref="MyGames"/>
  /// </summary>
  public event Action? OnMyGamesChanged;

  /// <summary>
  /// Fired after a successful handshake
  /// </summary>
  public event Action? OnConnected;

  /// <summary>
  /// Fired when the connection with the server gets lost
  /// </summary>
  public event Action? OnDisconnected;

  /// <summary>
  /// Fired after the server responds to <see cref="SetName"/>
  /// </summary>
  public event Action<bool>? OnNameSet;

  /// <summary>
  /// Fired after every authentication attempt, including the automatic ones after a reconnection
  /// </summary>
  /// <remarks>
  /// The argument is whether the client is logged in now; a false after a <see cref="Logout"/> is expected
  /// </remarks>
  public event Action<bool>? OnAuthenticated;

  /// <summary>
  /// Fired after the server responds to <see cref="OpenGame"/> or <see cref="RequestGameState"/>
  /// </summary>
  public event Action<GameStatePacket>? OnGameState;

  /// <summary>
  /// Fired after the server responds to <see cref="CloseGame"/> or <see cref="ResignGame"/>
  /// </summary>
  public event Action<MembershipResultPacket>? OnMembershipResult;

  /// <summary>
  /// Fired when the server sends the turn deadline of a game this client opened
  /// </summary>
  /// <remarks>
  /// The deadline is only meaningful against the server clock the packet carries: subtract the two to get
  /// how long is left, the clock of this machine could be off by anything
  /// </remarks>
  public event Action<GameClockPacket>? OnGameClock;

  /// <summary>
  /// Fired with the game id when the server broadcasts a change to a game this client opened
  /// </summary>
  /// <remarks>
  /// The client doesn't track the game state, ask for it again with <see cref="RequestGameState"/> if needed
  /// </remarks>
  public event Action<ulong>? OnGameChanged;

  /// <summary>
  /// Fired after the server responds to <see cref="CreateLobby"/>
  /// </summary>
  public event Action<LobbyActionResult, ulong>? OnLobbyCreated;

  /// <summary>
  /// Fired after the server responds to <see cref="JoinLobby"/>
  /// </summary>
  public event Action<LobbyActionResult, ulong>? OnLobbyJoined;

  /// <summary>
  /// Fired after the server responds to <see cref="LeaveLobby"/>
  /// </summary>
  public event Action<LobbyActionResult, ulong>? OnLobbyLeft;

  /// <summary>
  /// Fired after the server responds to <see cref="SetReady"/>
  /// </summary>
  public event Action<LobbyActionResult, ulong>? OnReadySet;

  /// <summary>
  /// Fired when the game of a lobby this player joined starts
  /// </summary>
  public event Action<GameStartedPacket>? OnGameStarted;

  /// <summary>
  /// Registers a handler for every packet the server sends
  /// </summary>
  public NetworkNode() {
    // register the player id and flush everything that waited for the handshake
    _dispatcher.Register<HandshakeResponsePacket>((_, packet) => ManageHandshakeResponse(packet));
    // confirm or refuse the name the player asked for
    _dispatcher.Register<SetNameResponsePacket>((_, packet) => ManageSetNameResponse(packet));
    // replace the lobby list with the one the server sent
    _dispatcher.Register<GetLobbiesResponsePacket>((_, packet) => ManageGetLobbiesResponse(packet));
    // report the outcome of a lobby action back to the scenes
    _dispatcher.Register<CreateLobbyResponsePacket>((_, packet) =>
      OnLobbyCreated?.Invoke(packet.Result, packet.LobbyId));
    _dispatcher.Register<JoinLobbyResponsePacket>((_, packet) => OnLobbyJoined?.Invoke(packet.Result, packet.LobbyId));
    _dispatcher.Register<LeaveLobbyResponsePacket>((_, packet) => OnLobbyLeft?.Invoke(packet.Result, packet.LobbyId));
    _dispatcher.Register<SetReadyResponsePacket>((_, packet) => OnReadySet?.Invoke(packet.Result, packet.LobbyId));
    // keep the local lobby list in sync with the server
    _dispatcher.Register<LobbyUpdatedPacket>((_, packet) => ManageLobbyUpdated(packet));
    _dispatcher.Register<LobbyDeletedPacket>((_, packet) => ManageLobbyDeleted(packet));
    // hand the game data over to the scenes
    _dispatcher.Register<GameStartedPacket>((_, packet) => ManageGameStarted(packet));
    // account handling
    _dispatcher.Register<AuthenticationPacket>((_, packet) => ManageAuthentication(packet));
    _dispatcher.Register<MyGamesPacket>((_, packet) => ManageMyGames(packet));
    _dispatcher.Register<GameStatePacket>((_, packet) => OnGameState?.Invoke(packet));
    _dispatcher.Register<MembershipResultPacket>((_, packet) => ManageMembershipResult(packet));
    _dispatcher.Register<GameClockPacket>((_, packet) => OnGameClock?.Invoke(packet));
    // the gameplay broadcasts of every opened game; the client only reports that something changed
    _dispatcher.Register<TroopMovedPacket>((_, packet) => OnGameChanged?.Invoke(packet.GameId));
    _dispatcher.Register<CombatPacket>((_, packet) => OnGameChanged?.Invoke(packet.GameId));
    _dispatcher.Register<TroopTrainedPacket>((_, packet) => OnGameChanged?.Invoke(packet.GameId));
    _dispatcher.Register<TechResearchedPacket>((_, packet) => OnGameChanged?.Invoke(packet.GameId));
    _dispatcher.Register<BuildingBuiltPacket>((_, packet) => OnGameChanged?.Invoke(packet.GameId));
    _dispatcher.Register<CityCapturedPacket>((_, packet) => OnGameChanged?.Invoke(packet.GameId));
    _dispatcher.Register<TurnStartedPacket>((_, packet) => OnGameChanged?.Invoke(packet.GameId));
    _dispatcher.Register<PlayerEliminatedPacket>((_, packet) => OnGameChanged?.Invoke(packet.GameId));
    _dispatcher.Register<GameOverPacket>((_, packet) => OnGameChanged?.Invoke(packet.GameId));
  }

  /// <summary>
  /// Takes over the shared connection and connects to the server if needed
  /// </summary>
  public override void _EnterTree() {
    base._EnterTree();
    Instance = this;

    if (AutoConnect && _connection == null) {
      ConnectToServer();
    }
  }

  /// <summary>
  /// Stops being the active instance when leaving the tree
  /// </summary>
  /// <remarks>
  /// Without this, <see cref="Instance"/> would keep referencing a freed node
  /// after a scene change to a scene without a <see cref="NetworkNode"/>
  /// </remarks>
  public override void _ExitTree() {
    base._ExitTree();

    if (Instance == this) {
      Instance = null;
    }
  }

  /// <summary>
  /// Processes the packets received from the server
  /// </summary>
  /// <param name="delta">ignored</param>
  public override void _PhysicsProcess(double delta) {
    // only the active instance processes the packets
    if (Instance != this) {
      return;
    }

    if (_handshakeDone && !_authenticated && !_authInFlight && _sessionToken != null && Time.GetTicksMsec() >= _resumeAt) {
      _resumeAt = ulong.MaxValue;
      _resuming = true;
      _authInFlight = true;
      Send(new ResumeSessionPacket { Token = _sessionToken });
    }
    var connection = _connection;
    if (connection == null) {
      // surface a connection attempt that failed before being established
      if (Interlocked.Exchange(ref _disconnectedFlag, 0) == 1) {
        ResetSessionState();
        OnDisconnected?.Invoke();
      }

      // retry periodically after a connection loss
      if (AutoConnect && DateTime.UtcNow >= _reconnectAt) {
        ConnectToServer();
      }

      return;
    }

    while (connection.IncomingPackets.TryDequeue(out var packet)) {
      ProcessPacket(packet);

      // stop if a handler disconnected
      if (_connection != connection) {
        return;
      }
    }

    // manage a disconnection signalled by the background read task
    if (Interlocked.Exchange(ref _disconnectedFlag, 0) == 1) {
      connection.Dispose();
      _connection = null;
      ResetSessionState();

      // wait before the first retry, the server could still be going down
      _reconnectAt = DateTime.UtcNow + RECONNECT_DELAY;

      OnLobbiesChanged?.Invoke();
      OnMyGamesChanged?.Invoke();
      OnDisconnected?.Invoke();
    }
  }

  /// <summary>
  /// Connects to the server
  /// </summary>
  /// <remarks>
  /// Called automatically when entering the tree if <see cref="AutoConnect"/> is enabled;
  /// does nothing if a shared connection already exists
  /// </remarks>
  public void ConnectToServer() {
    if (_connection != null) {
      return;
    }

    // schedule the next automatic retry in case this attempt fails
    _reconnectAt = DateTime.UtcNow + RECONNECT_DELAY;

    var (host, port) = ResolveServerAddress();

    // the saved session belongs to a single server, load the one of the server being connected to
    var serverKey = $"{host}:{port}";
    if (serverKey != _serverKey) {
      _serverKey = serverKey;
      _sessionToken = LoadSession(serverKey);
    }

    _ = ConnectToServerAsync(host, port);
  }

  /// <summary>
  /// Closes the shared connection and stops the automatic reconnection
  /// </summary>
  public void Disconnect() {
    _reconnectAt = DateTime.MaxValue;
    _connection?.Dispose();
    _connection = null;
    ResetSessionState();
    OnLobbiesChanged?.Invoke();
    OnMyGamesChanged?.Invoke();

    // consume the disconnection signalled by disposing the connection
    Interlocked.Exchange(ref _disconnectedFlag, 0);
  }

  /// <summary>
  /// Checks a username against the rules the server enforces
  /// </summary>
  /// <param name="username">the username to check</param>
  public static bool IsValidUsername(string username) =>
    username.Length is >= MIN_USERNAME_LENGTH and <= MAX_USERNAME_LENGTH &&
    username.All(character => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_');

  /// <summary>
  /// Checks a password against the rules the server enforces
  /// </summary>
  /// <param name="password">the password to check</param>
  public static bool IsValidPassword(string password) =>
    password.Length is >= MIN_PASSWORD_LENGTH and <= MAX_PASSWORD_LENGTH;

  /// <summary>
  /// Creates a new account and logs into it
  /// </summary>
  /// <param name="username">the username of the new account</param>
  /// <param name="password">the password of the new account; it never gets saved on disk</param>
  public void Register(string username, string password) {
    if (_authInFlight) return;
    _authInFlight = true;
    _resumeAt = ulong.MaxValue;
    _resuming = false;
    Send(new RegisterAccountPacket { Username = username, Password = password });
  }

  /// <summary>
  /// Logs into an existing account
  /// </summary>
  /// <param name="username">the username of the account</param>
  /// <param name="password">the password of the account; it never gets saved on disk</param>
  public void Login(string username, string password) {
    if (_authInFlight) return;
    _authInFlight = true;
    _resumeAt = ulong.MaxValue;
    _resuming = false;
    Send(new LoginPacket { Username = username, Password = password });
  }

  /// <summary>
  /// Logs out of the current account and forgets its saved session
  /// </summary>
  public void Logout() {
    Send(new LogoutPacket());

    // drop the token right away, the player asked not to be logged in again automatically
    ClearSession();
    _loggingOut = true;
    _authenticated = false;
    _accountId = 0;
    _accountName = "";
    _lobbies.Clear();
    _myGames.Clear();
    _authInFlight = false;
    _resumeAt = ulong.MaxValue;
  }

  /// <summary>
  /// Renames the player on the server
  /// </summary>
  /// <param name="name">the new player's name</param>
  /// <remarks>
  /// This only changes the display name, accounts are created with <see cref="Register"/>
  /// </remarks>
  public void SetName(string name) {
    _requestedName = name;
    Send(new SetNamePacket { Name = name });
  }

  /// <summary>
  /// Queries the server for all the lobbies
  /// </summary>
  public void RefreshLobbies() => Send(new GetLobbiesPacket());

  /// <summary>
  /// Queries the server for the games this account takes part in
  /// </summary>
  public void RefreshMyGames() => Send(new GetMyGamesPacket());

  /// <summary>
  /// Opens one of the account's games and subscribes to its updates
  /// </summary>
  /// <param name="gameId">the id of the game to open</param>
  public void OpenGame(ulong gameId) => Send(new JoinGamePacket { GameId = gameId });

  /// <summary>
  /// Closes an opened game without resigning from it
  /// </summary>
  /// <param name="gameId">the id of the game to close</param>
  public void CloseGame(ulong gameId) => Send(new LeaveGamePacket { GameId = gameId });

  /// <summary>
  /// Resigns from a game
  /// </summary>
  /// <remarks>
  /// This eliminates the player from the game and cannot be undone
  /// </remarks>
  /// <param name="gameId">the id of the game to resign from</param>
  public void ResignGame(ulong gameId) => Send(new ResignGamePacket { GameId = gameId });

  /// <summary>
  /// Asks the server for the full state of a game
  /// </summary>
  /// <param name="gameId">the id of the game</param>
  public void RequestGameState(ulong gameId) => Send(new GetGameStatePacket { GameId = gameId });

  /// <summary>
  /// Creates a new lobby; this player automatically joins it
  /// </summary>
  /// <param name="maxPlayers">max players that can join the lobby</param>
  /// <param name="tribe">the tribe chosen by this player</param>
  /// <param name="timerMode">turn timer of the game, 0 for live and 1 for daily</param>
  public void CreateLobby(uint maxPlayers, uint tribe, uint timerMode) =>
    Send(new CreateLobbyPacket { MaxPlayers = maxPlayers, WorldSize = 14, Tribe = tribe, TimerMode = timerMode });

  /// <summary>
  /// Joins an existing lobby
  /// </summary>
  /// <param name="lobbyId">the id of the lobby to join</param>
  /// <param name="tribe">the tribe chosen by this player</param>
  public void JoinLobby(ulong lobbyId, uint tribe) => Send(new JoinLobbyPacket { LobbyId = lobbyId, Tribe = tribe });

  /// <summary>
  /// Leaves a lobby this player joined before
  /// </summary>
  /// <param name="lobbyId">the id of the lobby to leave</param>
  public void LeaveLobby(ulong lobbyId) => Send(new LeaveLobbyPacket { LobbyId = lobbyId });

  /// <summary>
  /// Updates the ready state of this player in a lobby
  /// </summary>
  /// <param name="lobbyId">the id of the lobby</param>
  /// <param name="ready">the new ready state</param>
  public void SetReady(ulong lobbyId, bool ready) => Send(new SetReadyPacket { LobbyId = lobbyId, Ready = ready });

  /// <summary>
  /// Resolves the server host and port to connect to
  /// </summary>
  /// <remarks>
  /// Precedence: command line argument, environment variable, exported property
  /// </remarks>
  private (string, int) ResolveServerAddress() {
    var host = GetUserArg(HOST_ARG) ?? EnvOrNull(HOST_ENV) ?? Host;

    var port = Port;
    var portValue = GetUserArg(PORT_ARG) ?? EnvOrNull(PORT_ENV);
    if (portValue != null) {
      if (int.TryParse(portValue, out var parsed) && parsed is > 0 and <= ushort.MaxValue) {
        port = parsed;
      }
      else {
        GD.PushError($"Invalid server port: {portValue}, using {port}");
      }
    }

    return (host, port);
  }

  /// <summary>
  /// Returns the value of a <c>--name=value</c> user argument or null if missing
  /// </summary>
  /// <param name="name">the argument name, including the leading dashes</param>
  private static string? GetUserArg(string name) =>
    OS.GetCmdlineUserArgs()
      .Where(arg => arg.StartsWith($"{name}=", StringComparison.Ordinal))
      .Select(arg => arg[(name.Length + 1)..])
      .FirstOrDefault();

  private static string? EnvOrNull(string name) {
    var value = OS.GetEnvironment(name);
    return string.IsNullOrWhiteSpace(value) ? null : value;
  }

  private static async Task ConnectToServerAsync(string host, int port) {
    // clear a stale disconnection left over from a previous connection
    Interlocked.Exchange(ref _disconnectedFlag, 0);

    var connection = new ClientConnection(host, port);
    try {
      _connection = connection;
      connection.OnDisconnected += () => Interlocked.Exchange(ref _disconnectedFlag, 1);
      await connection.ConnectAsync();
      await connection.SendPacketAsync(new HandshakePacket { Version = NetworkConstants.VERSION });
    }
    catch (Exception e) {
      GD.PushError($"Error while connecting to {host}:{port}: {e}");

      // remove the connection and surface the failure to let a scene retry
      if (_connection == connection) {
        _connection = null;
        Interlocked.Exchange(ref _disconnectedFlag, 1);
      }

      connection.Dispose();
    }
  }

  /// <summary>
  /// Whether a packet is one of the few the server accepts before the client is logged in
  /// </summary>
  private static bool IsAccountPacket(IPacket packet) =>
    packet is RegisterAccountPacket or LoginPacket or ResumeSessionPacket or LogoutPacket;

  private static void Send(IPacket packet) {
    var connection = _connection;

    // without a connection there is no telling when the packet could go out, drop it
    if (connection == null) {
      if (IsAccountPacket(packet)) _authInFlight = false;
      return;
    }

    // the server kicks whoever talks before logging in, and replaying requests made against a session
    // that is gone would act on a state the player never saw: everything but the login waits for a scene to ask again
    if (!_handshakeDone || !_authenticated) {
      // connecting takes a moment, hold the login the player just asked for until the handshake is done
      if (!_handshakeDone && packet is RegisterAccountPacket or LoginPacket) {
        _pendingAuth = packet;
      }

      if (!_handshakeDone || !IsAccountPacket(packet)) {
        return;
      }
    }

    _ = SendAsync(connection, packet);
  }

  private static async Task SendAsync(ClientConnection connection, IPacket packet) {
    try {
      await connection.SendPacketAsync(packet);
    }
    catch (Exception e) {
      GD.PushError($"Error while sending packet: {e}");
    }
  }

  private void ProcessPacket(IPacket packet) {
    if (!_dispatcher.Dispatch(this, packet)) {
      // the server sending something the client never handles means the two disagree on the protocol
      GD.PushError($"Unhandled {packet.GetType().Name} from the server");
    }
  }

  private void ManageHandshakeResponse(HandshakeResponsePacket packet) {
    if (!packet.Ok) {
      GD.PushError("Server refused the connection: incompatible version");
      Disconnect();

      // let the scenes surface the refusal; retrying would fail the same way
      OnDisconnected?.Invoke();
      return;
    }

    _connectionId = packet.PlayerId;
    _handshakeDone = true;
    OnConnected?.Invoke();

    // a login the player asked for while connecting wins over the automatic resume
    if (_pendingAuth != null) {
      var pending = _pendingAuth;
      _pendingAuth = null;
      Send(pending);
      return;
    }

    // log in again after a reconnection, the server forgot this transport
    if (_sessionToken != null) {
      _resuming = true;
      Send(new ResumeSessionPacket { Token = _sessionToken });
    }
  }

  private void ManageSetNameResponse(SetNameResponsePacket packet) {
    if (packet.Ok) {
      _accountName = _requestedName ?? _accountName;
    }

    OnNameSet?.Invoke(packet.Ok);
  }

  private void ManageAuthentication(AuthenticationPacket packet) {
    _authInFlight = false;
    _retryable = packet.Retryable;
    if (_loggingOut && !packet.Ok) { _loggingOut = false; return; }
    var resuming = _resuming;
    _resuming = false;

    if (!packet.Ok) {
      // the server refused a token it handed out before: it expired or got revoked, ask the player to log in again
      if (resuming && !packet.Retryable) ClearSession();
      if (resuming && packet.Retryable) _resumeAt = Time.GetTicksMsec() + 3000;

      _authenticated = false;
      _accountId = 0;
      _accountName = "";
      _lobbies.Clear();
      _myGames.Clear();

      OnLobbiesChanged?.Invoke();
      OnMyGamesChanged?.Invoke();
      OnAuthenticated?.Invoke(false);
      return;
    }

    _authenticated = true;
    _accountId = packet.PlayerId;
    _accountName = packet.Name;
    SaveSession(packet.Token);

    OnAuthenticated?.Invoke(true);

    // get the initial lobby and game lists, the scenes are allowed to talk to the server now
    RefreshLobbies();
    RefreshMyGames();
  }

  private void ManageMyGames(MyGamesPacket packet) {
    _myGames.Clear();
    _myGames.AddRange(packet.GameIds);
    OnMyGamesChanged?.Invoke();
  }

  private void ManageMembershipResult(MembershipResultPacket packet) {
    OnMembershipResult?.Invoke(packet);

    // resigning removes the player from the game, the list of his games changed
    RefreshMyGames();
  }

  private void ManageGameStarted(GameStartedPacket packet) {
    OnGameStarted?.Invoke(packet);

    // the lobby became a game this account takes part in
    RefreshMyGames();
  }

  private void ManageGetLobbiesResponse(GetLobbiesResponsePacket packet) {
    _lobbies.Clear();
    _lobbies.AddRange(packet.Lobbies);
    OnLobbiesChanged?.Invoke();
  }

  private void ManageLobbyUpdated(LobbyUpdatedPacket packet) {
    // replace the old lobby data in place if present
    var oldIndex = _lobbies.FindIndex(lobby => lobby.Id == packet.Lobby.Id);
    if (oldIndex >= 0) {
      _lobbies[oldIndex] = packet.Lobby;
    }
    else {
      _lobbies.Add(packet.Lobby);
    }

    OnLobbiesChanged?.Invoke();
  }

  private void ManageLobbyDeleted(LobbyDeletedPacket packet) {
    if (_lobbies.RemoveAll(lobby => lobby.Id == packet.LobbyId) > 0) {
      OnLobbiesChanged?.Invoke();
    }
  }

  /// <summary>
  /// Forgets everything tied to a connection, keeping the session token to resume with
  /// </summary>
  private static void ResetSessionState() {
    _authInFlight = false;
    _loggingOut = false;
    _resumeAt = ulong.MaxValue;
    _handshakeDone = false;
    _authenticated = false;
    _resuming = false;
    _pendingAuth = null;
    _lobbies.Clear();
    _myGames.Clear();
  }

  /// <summary>
  /// Returns the session token saved for a server, or null when there is none
  /// </summary>
  /// <param name="serverKey">the <c>host:port</c> of the server</param>
  private static string? LoadSession(string serverKey) {
    var config = new ConfigFile();
    if (config.Load(SESSION_FILE) != Error.Ok) {
      return null;
    }

    var token = config.GetValue(serverKey, TOKEN_KEY, "").AsString();
    return string.IsNullOrEmpty(token) ? null : token;
  }

  /// <summary>
  /// Saves the session token of the server this client is connected to
  /// </summary>
  /// <param name="token">the opaque token the server handed out</param>
  private static void SaveSession(string token) {
    _sessionToken = token;

    var config = new ConfigFile();

    // load first, the file holds the tokens of the other servers too
    config.Load(SESSION_FILE);
    config.SetValue(_serverKey, TOKEN_KEY, token);

    var error = config.Save(SESSION_FILE);
    if (error != Error.Ok) {
      GD.PushError($"Cannot save the session token: {error}");
      return;
    }

    RestrictSessionFilePermissions();
  }

  /// <summary>
  /// Forgets the session token of the server this client is connected to
  /// </summary>
  private static void ClearSession() {
    _sessionToken = null;

    var config = new ConfigFile();
    if (config.Load(SESSION_FILE) != Error.Ok || !config.HasSection(_serverKey)) {
      return;
    }

    config.EraseSection(_serverKey);

    var error = config.Save(SESSION_FILE);
    if (error != Error.Ok) {
      GD.PushError($"Cannot clear the session token: {error}");
    }
  }

  /// <summary>
  /// Keeps the session file readable by this user only
  /// </summary>
  /// <remarks>
  /// A session token is as good as a password, and the user data directory is world readable on most systems;
  /// Windows has no equivalent of the unix file mode, there the file keeps the permissions it inherits
  /// </remarks>
  private static void RestrictSessionFilePermissions() {
    if (OperatingSystem.IsWindows()) {
      return;
    }

    try {
      File.SetUnixFileMode(ProjectSettings.GlobalizePath(SESSION_FILE), UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    catch (Exception e) {
      GD.PushWarning($"Cannot restrict the permissions of the session file: {e.Message}");
    }
  }
}
