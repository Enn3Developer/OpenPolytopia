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

  private const string HOST = "enn3.ovh";
  private const int PORT = NetworkConstants.DEFAULT_PORT;

  private ClientConnection? _connection;

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
    _ = ConnectToServerAsync();
  }

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
      _connection = new ClientConnection(HOST, PORT);
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
