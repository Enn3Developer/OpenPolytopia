namespace OpenPolytopia;

using Godot;
using Godot.Collections;

public partial class Lobby : Control {
  private const string ADDRESS = "enn3.ovh";
  private const int PORT = 6969;

  /// <summary>
  /// Instance of the lobby
  /// </summary>
  public static Lobby Instance = null!;

  /// <summary>
  /// Fired when a player connects to the lobby
  /// </summary>
  [Signal]
  public delegate void PlayerConnectedEventHandler(int id, PlayerData playerData);

  /// <summary>
  /// Fired when a player disconnects from the lobby
  /// </summary>
  [Signal]
  public delegate void PlayerDisconnectedEventHandler(int id);

  /// <summary>
  /// Fired when the server closes
  /// </summary>
  [Signal]
  public delegate void ServerDisconnectedEventHandler();

  /// <summary>
  /// The game scene to switch to
  /// </summary>
  public PackedScene? GameScene;

  private uint _playersInLobby;
  private uint _playersStarted;
  private Dictionary<long, PlayerData> _playerData = new();

  public override void _Ready() {
    // Set the instance of the lobby to the current lobby
    Instance = this;

    // Connects all the signals
    Multiplayer.PeerConnected += OnPlayerConnected;
    Multiplayer.PeerDisconnected += OnPlayerDisconnected;
    Multiplayer.ConnectedToServer += OnConnectOk;
    Multiplayer.ConnectionFailed += OnConnectionFail;
    Multiplayer.ServerDisconnected += OnServerDisconnected;

    // if it is the server, create the lobby
    if (OS.HasFeature("dedicated_server")) {
      CreateGame();
    }
    // else, it's a player then join the lobby
    else {
      // TODO: choose tribe before joining game
      JoinGame();
    }
  }

  /// <summary>
  /// Changes the scene to the game scene
  /// </summary>
  /// <remarks>
  /// This is called on all the players
  /// </remarks>
  [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  private void StartGame() {
    // Check if the game scene is valid
    if (GameScene == null) {
      GD.PrintErr("GameScene is null");
      return;
    }

    GetTree().ChangeSceneToPacked(GameScene);
  }

  /// <summary>
  /// Called when a player has loaded the game scene
  /// </summary>
  /// <remarks>
  /// This executes both on the player calling it and the server, so we check if we are the server
  /// then check if all players are connected, if yes we start the game
  /// </remarks>
  [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  private void PlayerLoaded() {
    if (!Multiplayer.IsServer()) {
      return;
    }

    if (++_playersStarted == _playersInLobby) {
      GetNode<PolyGame>("/root/PolyGame").StartGame();
    }
  }

  /// <summary>
  /// Joins the lobby as a player
  /// </summary>
  private void JoinGame() {
    var peer = new ENetMultiplayerPeer();
    var error = peer.CreateClient(ADDRESS, PORT);

    if (error != Error.Ok) {
      GD.PrintErr($"Error happened during client creation: {error}");
    }

    Multiplayer.MultiplayerPeer = peer;
  }

  /// <summary>
  /// Creates the lobby as the server
  /// </summary>
  private void CreateGame() {
    var peer = new ENetMultiplayerPeer();
    var error = peer.CreateServer(PORT);

    if (error != Error.Ok) {
      GD.PrintErr($"Error happened during server creation: {error}");
    }

    Multiplayer.MultiplayerPeer = peer;
  }

  /// <summary>
  /// Registers the connected players
  /// </summary>
  /// <remarks>
  /// This is called on all peers (server and players) for each player that connects to the lobby
  /// </remarks>
  /// <param name="playerData">the player data of the newly connected player</param>
  [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  private void RegisterPlayer(PlayerData playerData) {
    var id = Multiplayer.GetRemoteSenderId();
    if (id == 1) {
      return;
    }

    _playerData[id] = playerData;
    EmitSignal(SignalName.PlayerConnected, id);
  }

  /// <summary>
  /// Called when a new player connects
  /// </summary>
  /// <param name="id">the id of the new player connected to the lobby</param>
  private void OnPlayerConnected(long id) {
    _playersInLobby++;

    // If we're the server, skip the registration phase
    if (id == 1) {
      return;
    }

    RpcId(id, MethodName.RegisterPlayer, PlayerData.Instance);
  }

  /// <summary>
  /// Called when a player disconnects
  /// </summary>
  /// <param name="id">the id of the player disconnected</param>
  private void OnPlayerDisconnected(long id) {
    _playersInLobby--;

    // If we're the server, skip the signal
    if (id == 1) {
      return;
    }

    _playerData.Remove(id);
    EmitSignal(SignalName.PlayerDisconnected, id);
  }

  /// <summary>
  /// Called on the player connecting when connected to the lobby
  /// </summary>
  private void OnConnectOk() {
    var id = Multiplayer.GetUniqueId();

    // If we're the server, skip the signal
    if (id == 1) {
      return;
    }

    _playerData[id] = PlayerData.Instance;
    EmitSignal(SignalName.PlayerConnected, id, PlayerData.Instance);
  }

  /// <summary>
  /// Called when the connection fails
  /// </summary>
  private void OnConnectionFail() => Multiplayer.MultiplayerPeer = null;

  /// <summary>
  /// Called when the server disconnects
  /// </summary>
  private void OnServerDisconnected() {
    Multiplayer.MultiplayerPeer = null;
    _playerData.Clear();
    EmitSignal(SignalName.ServerDisconnected);
  }
}
