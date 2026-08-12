namespace OpenPolytopia;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Common.Network;
using Common.Network.Packets;
using Godot;

/// <summary>
/// Manages the connection with the game server.
/// <br/>
/// Add this node to every scene that talks to the server: the underlying connection is shared
/// between all the instances and survives scene changes, so the first node to enter the tree
/// connects and the following ones reuse the same connection.
/// <br/>
/// Packets are received in background and processed on the main thread
/// in <see cref="_PhysicsProcess"/>, so every event is safe to use with Godot nodes
/// </summary>
[GlobalClass]
public partial class NetworkNode : Node {
  /// <summary>
  /// The instance currently pumping the connection, i.e. the last one that entered the tree
  /// </summary>
  public static NetworkNode Instance { get; private set; } = null!;

  // overrides usable without touching the scene:
  // OPENPOLYTOPIA_SERVER_HOST=... or `godot -- --server-host=...` (same for port)
  private const string HOST_ENV = "OPENPOLYTOPIA_SERVER_HOST";
  private const string PORT_ENV = "OPENPOLYTOPIA_SERVER_PORT";
  private const string HOST_ARG = "--server-host";
  private const string PORT_ARG = "--server-port";

  // the connection and the session state are shared between all NetworkNode instances
  // so they survive scene changes; only app shutdown or Disconnect() close the connection
  private static ClientConnection? _connection;
  private static bool _handshakeDone;
  private static uint _playerId;
  private static readonly ObservableCollection<LobbyData> _lobbies = [];
  private static int _disconnectedFlag;

  /// <summary>
  /// Host to connect to; editable in the inspector.
  /// Overridable with the <c>--server-host=</c> command line argument
  /// or the <c>OPENPOLYTOPIA_SERVER_HOST</c> environment variable
  /// </summary>
  [Export]
  public string Host { get; set; } = "enn3.ovh";

  /// <summary>
  /// Port to connect to; editable in the inspector.
  /// Overridable with the <c>--server-port=</c> command line argument
  /// or the <c>OPENPOLYTOPIA_SERVER_PORT</c> environment variable
  /// </summary>
  [Export(PropertyHint.Range, "1,65535")]
  public int Port { get; set; } = NetworkConstants.DEFAULT_PORT;

  /// <summary>
  /// If true, the node connects to the server as soon as it enters the tree
  /// (unless a shared connection already exists)
  /// </summary>
  [Export]
  public bool AutoConnect { get; set; } = true;

  /// <summary>
  /// Id assigned to this client by the server; valid after <see cref="OnConnected"/>
  /// </summary>
  public uint PlayerId => _playerId;

  /// <summary>
  /// true after a successful handshake with the server
  /// </summary>
  public bool Connected => _handshakeDone;

  /// <summary>
  /// Lobbies data; it updates when the server broadcasts lobby changes
  /// </summary>
  public ObservableCollection<LobbyData> Lobbies => _lobbies;

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
  /// Take over the pumping of the shared connection and connect if needed
  /// </summary>
  public override void _EnterTree() {
    base._EnterTree();
    Instance = this;

    if (AutoConnect && _connection == null) {
      ConnectToServer();
    }
  }

  /// <summary>
  /// Process messages received from the server
  /// </summary>
  /// <param name="delta">ignored</param>
  public override void _PhysicsProcess(double delta) {
    // only the active instance pumps the shared connection
    if (Instance != this || _connection == null) {
      return;
    }

    while (_connection.IncomingPackets.TryDequeue(out var packet)) {
      ProcessPacket(packet);
    }

    // manage a disconnection signalled by the background read task
    if (Interlocked.Exchange(ref _disconnectedFlag, 0) == 1) {
      _handshakeDone = false;
      _connection.Dispose();
      _connection = null;
      _lobbies.Clear();
      OnDisconnected?.Invoke();
    }
  }

  /// <summary>
  /// Connects to the server; called automatically when entering the tree if
  /// <see cref="AutoConnect"/> is enabled.
  /// Does nothing if a shared connection already exists
  /// </summary>
  public void ConnectToServer() {
    if (_connection != null) {
      return;
    }

    var (host, port) = ResolveServerAddress();
    _ = ConnectToServerAsync(host, port);
  }

  /// <summary>
  /// Closes the shared connection
  /// </summary>
  public void Disconnect() {
    _connection?.Dispose();
    _connection = null;
    _handshakeDone = false;
    _lobbies.Clear();
  }

  /// <summary>
  /// Registers the player on the server or renames him
  /// </summary>
  /// <param name="name">the new player's name</param>
  public void SetName(string name) => Send(new SetNamePacket { Name = name });

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
  /// Resolves the server host and port to connect to.
  /// Precedence: command line argument, environment variable, exported property
  /// </summary>
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
  /// Returns the value of a <c>--name=value</c> user argument (the ones after <c>--</c>) or null
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
    var connection = new ClientConnection(host, port);
    try {
      _connection = connection;
      connection.OnDisconnected += () => Interlocked.Exchange(ref _disconnectedFlag, 1);
      await connection.ConnectAsync();
      await connection.SendPacketAsync(new HandshakePacket { Version = NetworkConstants.VERSION });
    }
    catch (Exception e) {
      GD.PushError($"Error while connecting to {host}:{port}: {e}");

      // throw the connection away so a new node (or a manual call) can retry
      if (_connection == connection) {
        _connection = null;
      }

      connection.Dispose();
    }
  }

  private void Send(IPacket packet) {
    if (_connection == null) {
      GD.PushError("Not connected to the server");
      return;
    }

    _ = SendAsync(_connection, packet);
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
    switch (packet) {
      case HandshakeResponsePacket handshakeResponse:
        if (!handshakeResponse.Ok) {
          GD.PushError("Server refused the connection: incompatible version");
          Disconnect();
          break;
        }

        _playerId = handshakeResponse.PlayerId;
        _handshakeDone = true;
        OnConnected?.Invoke();

        // get the initial lobby list
        RefreshLobbies();
        break;
      case SetNameResponsePacket setNameResponse:
        OnNameSet?.Invoke(setNameResponse.Ok);
        break;
      case GetLobbiesResponsePacket lobbiesResponse:
        _lobbies.Clear();
        foreach (var lobby in lobbiesResponse.Lobbies) {
          _lobbies.Add(lobby);
        }

        break;
      case CreateLobbyResponsePacket createLobbyResponse:
        OnLobbyCreated?.Invoke(createLobbyResponse.Result, createLobbyResponse.LobbyId);
        break;
      case JoinLobbyResponsePacket joinLobbyResponse:
        OnLobbyJoined?.Invoke(joinLobbyResponse.Result, joinLobbyResponse.LobbyId);
        break;
      case LeaveLobbyResponsePacket leaveLobbyResponse:
        OnLobbyLeft?.Invoke(leaveLobbyResponse.Result, leaveLobbyResponse.LobbyId);
        break;
      case SetReadyResponsePacket setReadyResponse:
        OnReadySet?.Invoke(setReadyResponse.Result, setReadyResponse.LobbyId);
        break;
      case LobbyUpdatedPacket lobbyUpdated:
        // remove the old lobby data if present
        var oldLobby = _lobbies.FirstOrDefault(lobby => lobby.Id == lobbyUpdated.Lobby.Id);
        if (oldLobby != null) {
          _lobbies.Remove(oldLobby);
        }

        // add back the lobby data to force the list to emit the event
        _lobbies.Add(lobbyUpdated.Lobby);
        break;
      case LobbyDeletedPacket lobbyDeleted:
        var deletedLobby = _lobbies.FirstOrDefault(lobby => lobby.Id == lobbyDeleted.LobbyId);
        if (deletedLobby != null) {
          _lobbies.Remove(deletedLobby);
        }

        break;
      case GameStartedPacket gameStarted:
        OnGameStarted?.Invoke(gameStarted);
        break;
    }
  }
}
