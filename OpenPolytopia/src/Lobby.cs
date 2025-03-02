namespace OpenPolytopia;

using Godot;
using Godot.Collections;

public partial class Lobby : Control {
  private const string ADDRESS = "enn3.ovh";
  private const int PORT = 6969;

  public static Lobby Instance = null!;

  [Signal]
  public delegate void PlayerConnectedEventHandler(int id, PlayerData playerData);

  [Signal]
  public delegate void PlayerDisconnectedEventHandler(int id);

  [Signal]
  public delegate void ServerDisconnectedEventHandler();

  public PackedScene? GameScene;

  private uint _playersInLobby;
  private uint _playersStarted;
  private Dictionary<long, PlayerData> _playerData = new();

  public override void _Ready() {
    Instance = this;

    Multiplayer.PeerConnected += OnPlayerConnected;
    Multiplayer.PeerDisconnected += OnPlayerDisconnected;
    Multiplayer.ConnectedToServer += OnConnectOk;
    Multiplayer.ConnectionFailed += OnConnectionFail;
    Multiplayer.ServerDisconnected += OnServerDisconnected;

    if (OS.HasFeature("dedicated_server")) {
      CreateGame();
    }
    else {
      // TODO: choose tribe before joining game
      JoinGame();
    }
  }

  [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  private void StartGame() {
    if (GameScene == null) {
      GD.PrintErr("GameScene is null");
      return;
    }

    GetTree().ChangeSceneToPacked(GameScene);
  }

  [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  private void PlayerLoaded() {
    if (!Multiplayer.IsServer()) {
      return;
    }

    if (++_playersStarted == _playersInLobby) {
      GetNode<PolyGame>("/root/PolyGame").StartGame();
    }
  }

  private void JoinGame() {
    var peer = new ENetMultiplayerPeer();
    var error = peer.CreateClient(ADDRESS, PORT);

    if (error != Error.Ok) {
      GD.PrintErr($"Error happened during client creation: {error}");
    }

    Multiplayer.MultiplayerPeer = peer;
  }

  private void CreateGame() {
    var peer = new ENetMultiplayerPeer();
    var error = peer.CreateServer(PORT);

    if (error != Error.Ok) {
      GD.PrintErr($"Error happened during server creation: {error}");
    }

    Multiplayer.MultiplayerPeer = peer;
    EmitSignal(SignalName.PlayerConnected, 1);
  }

  [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  private void RegisterPlayer(PlayerData playerData) {
    var id = Multiplayer.GetRemoteSenderId();
    if (id == 1) {
      return;
    }

    _playerData[id] = playerData;
    EmitSignal(SignalName.PlayerConnected, id);
  }

  private void OnPlayerConnected(long id) {
    _playersInLobby++;

    if (id == 1) {
      return;
    }

    RpcId(id, MethodName.RegisterPlayer, PlayerData.Instance);
  }

  private void OnPlayerDisconnected(long id) {
    _playersInLobby--;

    if (id == 1) {
      return;
    }

    _playerData.Remove(id);
    EmitSignal(SignalName.PlayerDisconnected, id);
  }

  private void OnConnectOk() {
    var id = Multiplayer.GetUniqueId();
    if (id == 1) {
      return;
    }

    _playerData[id] = PlayerData.Instance;
    EmitSignal(SignalName.PlayerConnected, id, PlayerData.Instance);
  }

  private void OnConnectionFail() => Multiplayer.MultiplayerPeer = null;

  private void OnServerDisconnected() {
    Multiplayer.MultiplayerPeer = null;
    _playerData.Clear();
    EmitSignal(SignalName.ServerDisconnected);
  }
}
