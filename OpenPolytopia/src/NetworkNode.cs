namespace OpenPolytopia;

using System;
using System.Collections.Generic;
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

  private static readonly TimeSpan RECONNECT_DELAY = TimeSpan.FromSeconds(3);

  private const string HOST_ENV = "OPENPOLYTOPIA_SERVER_HOST";
  private const string PORT_ENV = "OPENPOLYTOPIA_SERVER_PORT";
  private const string HOST_ARG = "--server-host";
  private const string PORT_ARG = "--server-port";

  // shared between all the instances so the connection survives scene changes
  private static ClientConnection? _connection;
  private static bool _handshakeDone;
  private static uint _playerId;
  private static readonly List<LobbyData> _lobbies = [];
  private static readonly Queue<IPacket> _pendingPackets = new();
  private static int _disconnectedFlag;
  private static DateTime _reconnectAt = DateTime.MaxValue;
  private static string? _requestedName;
  private static string? _acceptedName;

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
  /// Id assigned to this client by the server
  /// </summary>
  /// <remarks>
  /// Valid after <see cref="OnConnected"/> gets fired
  /// </remarks>
  public uint PlayerId => _playerId;

  /// <summary>
  /// true after a successful handshake with the server
  /// </summary>
  public bool Connected => _handshakeDone;

  /// <summary>
  /// Lobbies data; it updates when the server broadcasts lobby changes
  /// </summary>
  public IReadOnlyList<LobbyData> Lobbies => _lobbies;

  /// <summary>
  /// Fired once after every change to <see cref="Lobbies"/>
  /// </summary>
  public event Action? OnLobbiesChanged;

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
    _dispatcher.Register<GameStartedPacket>((_, packet) => OnGameStarted?.Invoke(packet));
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

    var connection = _connection;
    if (connection == null) {
      // surface a connection attempt that failed before being established
      if (Interlocked.Exchange(ref _disconnectedFlag, 0) == 1) {
        _pendingPackets.Clear();
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
      _handshakeDone = false;
      connection.Dispose();
      _connection = null;
      _lobbies.Clear();
      _pendingPackets.Clear();

      // wait before the first retry, the server could still be going down
      _reconnectAt = DateTime.UtcNow + RECONNECT_DELAY;

      OnLobbiesChanged?.Invoke();
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
    _ = ConnectToServerAsync(host, port);
  }

  /// <summary>
  /// Closes the shared connection and stops the automatic reconnection
  /// </summary>
  public void Disconnect() {
    _reconnectAt = DateTime.MaxValue;
    _connection?.Dispose();
    _connection = null;
    _handshakeDone = false;
    _lobbies.Clear();
    _pendingPackets.Clear();
    OnLobbiesChanged?.Invoke();

    // consume the disconnection signalled by disposing the connection
    Interlocked.Exchange(ref _disconnectedFlag, 0);
  }

  /// <summary>
  /// Registers the player on the server or renames him
  /// </summary>
  /// <param name="name">the new player's name</param>
  public void SetName(string name) {
    _requestedName = name;
    Send(new SetNamePacket { Name = name });
  }

  /// <summary>
  /// Queries the server for all the lobbies
  /// </summary>
  public void RefreshLobbies() => Send(new GetLobbiesPacket());

  /// <summary>
  /// Creates a new lobby; this player automatically joins it
  /// </summary>
  /// <param name="maxPlayers">max players that can join the lobby</param>
  /// <param name="tribe">the tribe chosen by this player</param>
  public void CreateLobby(uint maxPlayers, uint tribe) =>
    Send(new CreateLobbyPacket { MaxPlayers = maxPlayers, Tribe = tribe });

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

  private static void Send(IPacket packet) {
    var connection = _connection;

    // without a connection there is no telling when the packet could go out, drop it
    if (connection == null) {
      return;
    }

    // queue the packet while the handshake is in flight, connecting takes a moment;
    // the queue gets flushed after the handshake and dropped on a failed connection
    if (!_handshakeDone) {
      _pendingPackets.Enqueue(packet);
      return;
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

    _playerId = packet.PlayerId;
    _handshakeDone = true;
    OnConnected?.Invoke();

    // register again after a reconnection, the server forgot this player
    if (_acceptedName != null) {
      Send(new SetNamePacket { Name = _acceptedName });
    }

    // send the packets queued while connecting; stop if a handler disconnected
    while (_handshakeDone && _pendingPackets.TryDequeue(out var pending)) {
      Send(pending);
    }

    // get the initial lobby list
    RefreshLobbies();
  }

  private void ManageSetNameResponse(SetNameResponsePacket packet) {
    if (packet.Ok) {
      // remember the name to register again after a reconnection
      _acceptedName = _requestedName;
    }

    OnNameSet?.Invoke(packet.Ok);
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
}
