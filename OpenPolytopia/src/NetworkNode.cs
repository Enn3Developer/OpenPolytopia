namespace OpenPolytopia;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Network;
using Common.Network.Packets;
using Godot;

/// <summary>
/// Manages the connection with the game server.
/// <br/>
/// Packets are received in background and processed on the main thread
/// in <see cref="_PhysicsProcess"/>, so every event is safe to use with Godot nodes
/// </summary>
public partial class NetworkNode : Node {
  /// <summary>
  /// Public singleton
  /// </summary>
  public static NetworkNode Instance { get; private set; } = null!;

  private const string DEFAULT_HOST = "enn3.ovh";

  // project settings editable in the editor, see [open_polytopia] in project.godot
  private const string HOST_SETTING = "open_polytopia/network/server_host";
  private const string PORT_SETTING = "open_polytopia/network/server_port";

  // overrides usable without touching the project:
  // OPENPOLYTOPIA_SERVER_HOST=... or `godot -- --server-host=...` (same for port)
  private const string HOST_ENV = "OPENPOLYTOPIA_SERVER_HOST";
  private const string PORT_ENV = "OPENPOLYTOPIA_SERVER_PORT";
  private const string HOST_ARG = "--server-host";
  private const string PORT_ARG = "--server-port";

  private ClientConnection? _connection;

  /// <summary>
  /// Host the client connects to
  /// </summary>
  public string Host { get; private set; } = DEFAULT_HOST;

  /// <summary>
  /// Port the client connects to
  /// </summary>
  public int Port { get; private set; } = NetworkConstants.DEFAULT_PORT;

  /// <summary>
  /// Id assigned to this client by the server; valid after <see cref="OnConnected"/>
  /// </summary>
  public uint PlayerId { get; private set; }

  /// <summary>
  /// true after a successful handshake with the server
  /// </summary>
  public bool Connected { get; private set; }

  /// <summary>
  /// Lobbies data; it updates when the server broadcasts lobby changes
  /// </summary>
  public readonly ObservableCollection<LobbyData> Lobbies = [];

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
  /// Initialize the connection
  /// </summary>
  public override void _EnterTree() {
    base._EnterTree();
    Instance = this;
    ResolveServerAddress();
    _ = ConnectToServerAsync();
  }

  /// <summary>
  /// Resolves the server host and port to connect to.
  /// Precedence: command line argument, environment variable, project setting, default
  /// </summary>
  private void ResolveServerAddress() {
    var host = GetUserArg(HOST_ARG) ?? EnvOrNull(HOST_ENV) ??
               SettingOrNull(HOST_SETTING)?.AsString();
    if (!string.IsNullOrWhiteSpace(host)) {
      Host = host;
    }

    var portValue = GetUserArg(PORT_ARG) ?? EnvOrNull(PORT_ENV);
    if (portValue == null && SettingOrNull(PORT_SETTING) is { } portSetting) {
      Port = portSetting.AsInt32();
    }
    else if (portValue != null) {
      if (int.TryParse(portValue, out var port) && port is > 0 and <= ushort.MaxValue) {
        Port = port;
      }
      else {
        GD.PushError($"Invalid server port: {portValue}, using {Port}");
      }
    }
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

  private static Variant? SettingOrNull(string name) =>
    ProjectSettings.HasSetting(name) ? ProjectSettings.GetSetting(name) : (Variant?)null;

  /// <summary>
  /// Disconnect from the server
  /// </summary>
  public override void _ExitTree() {
    base._ExitTree();
    _connection?.Dispose();
    _connection = null;
  }

  /// <summary>
  /// Process messages received from the server
  /// </summary>
  /// <param name="delta">ignored</param>
  public override void _PhysicsProcess(double delta) {
    if (_connection == null) {
      return;
    }

    while (_connection.IncomingPackets.TryDequeue(out var packet)) {
      ProcessPacket(packet);
    }
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

  private async Task ConnectToServerAsync() {
    try {
      _connection = new ClientConnection(Host, Port);
      _connection.OnDisconnected += () => {
        Connected = false;
        OnDisconnected?.Invoke();
      };
      await _connection.ConnectAsync();
      await _connection.SendPacketAsync(new HandshakePacket { Version = NetworkConstants.VERSION });
    }
    catch (Exception e) {
      GD.PushError($"Error while connecting: {e}");
    }
  }

  private void Send(IPacket packet) {
    if (_connection == null) {
      GD.PushError("Not connected to the server");
      return;
    }

    _ = SendAsync(packet);
  }

  private async Task SendAsync(IPacket packet) {
    try {
      await _connection!.SendPacketAsync(packet);
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
          _connection?.Disconnect();
          break;
        }

        PlayerId = handshakeResponse.PlayerId;
        Connected = true;
        OnConnected?.Invoke();

        // get the initial lobby list
        RefreshLobbies();
        break;
      case SetNameResponsePacket setNameResponse:
        OnNameSet?.Invoke(setNameResponse.Ok);
        break;
      case GetLobbiesResponsePacket lobbiesResponse:
        Lobbies.Clear();
        foreach (var lobby in lobbiesResponse.Lobbies) {
          Lobbies.Add(lobby);
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
        var oldLobby = Lobbies.FirstOrDefault(lobby => lobby.Id == lobbyUpdated.Lobby.Id);
        if (oldLobby != null) {
          Lobbies.Remove(oldLobby);
        }

        // add back the lobby data to force the list to emit the event
        Lobbies.Add(lobbyUpdated.Lobby);
        break;
      case LobbyDeletedPacket lobbyDeleted:
        var deletedLobby = Lobbies.FirstOrDefault(lobby => lobby.Id == lobbyDeleted.LobbyId);
        if (deletedLobby != null) {
          Lobbies.Remove(deletedLobby);
        }

        break;
      case GameStartedPacket gameStarted:
        OnGameStarted?.Invoke(gameStarted);
        break;
    }
  }
}
