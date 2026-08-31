namespace OpenPolytopia;

using System;
using System.Linq;
using Common;
using Common.Network.Packets;
using Godot;

/// <summary>
/// Lists the lobbies on the server
/// </summary>
/// <remarks>
/// Lets the player create, join and leave a lobby and mark himself as ready
/// </remarks>
public partial class Lobby : Control {
  private const uint DEFAULT_MAX_PLAYERS = 4;

  private ItemList _lobbyList = null!;
  private OptionButton _tribeButton = null!;
  private Button _createButton = null!;
  private Button _joinButton = null!;
  private Button _leaveButton = null!;
  private CheckButton _readyButton = null!;
  private Label _statusLabel = null!;

  private NetworkNode _network = null!;
  private ulong _joinedLobbyId;
  private bool _joined;

  public override void _Ready() {
    BuildUi();

    // grab the instance once, the node could leave the tree before this scene
    _network = NetworkNode.Instance!;
    var network = _network;

    network.OnLobbiesChanged += OnLobbiesChanged;
    network.OnLobbyCreated += OnLobbyCreated;
    network.OnLobbyJoined += OnLobbyJoined;
    network.OnLobbyLeft += OnLobbyLeft;
    network.OnReadySet += OnReadySet;
    network.OnGameStarted += OnGameStarted;
    network.OnDisconnected += OnNetworkDisconnected;

    network.RefreshLobbies();
    RefreshList();
    UpdateButtons();
  }

  public override void _ExitTree() {
    base._ExitTree();

    var network = _network;
    network.OnLobbiesChanged -= OnLobbiesChanged;
    network.OnLobbyCreated -= OnLobbyCreated;
    network.OnLobbyJoined -= OnLobbyJoined;
    network.OnLobbyLeft -= OnLobbyLeft;
    network.OnReadySet -= OnReadySet;
    network.OnGameStarted -= OnGameStarted;
    network.OnDisconnected -= OnNetworkDisconnected;
  }

  private void BuildUi() {
    var root = new VBoxContainer();
    root.SetAnchorsPreset(LayoutPreset.FullRect);
    root.AddThemeConstantOverride("separation", 8);
    AddChild(root);

    var title = new Label { Text = "Lobbies", HorizontalAlignment = HorizontalAlignment.Center };
    title.AddThemeFontSizeOverride("font_size", 32);
    root.AddChild(title);

    var topBar = new HBoxContainer();
    root.AddChild(topBar);

    _tribeButton = new OptionButton();
    foreach (var tribe in Enum.GetValues<TribeType>()) {
      _tribeButton.AddItem(tribe.ToString(), (int)tribe);
    }

    _tribeButton.Select(0);
    topBar.AddChild(_tribeButton);

    _createButton = new Button { Text = "Create lobby" };
    _createButton.Pressed += OnCreatePressed;
    topBar.AddChild(_createButton);

    var refreshButton = new Button { Text = "Refresh" };
    refreshButton.Pressed += () => _network.RefreshLobbies();
    topBar.AddChild(refreshButton);

    _lobbyList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
    _lobbyList.ItemSelected += _ => UpdateButtons();
    root.AddChild(_lobbyList);

    var bottomBar = new HBoxContainer();
    root.AddChild(bottomBar);

    _joinButton = new Button { Text = "Join" };
    _joinButton.Pressed += OnJoinPressed;
    bottomBar.AddChild(_joinButton);

    _leaveButton = new Button { Text = "Leave" };
    _leaveButton.Pressed += OnLeavePressed;
    bottomBar.AddChild(_leaveButton);

    _readyButton = new CheckButton { Text = "Ready" };
    _readyButton.Toggled += OnReadyToggled;
    bottomBar.AddChild(_readyButton);

    _statusLabel = new Label();
    bottomBar.AddChild(_statusLabel);
  }

  private void OnLobbiesChanged() {
    RefreshList();
    UpdateButtons();
  }

  private void OnNetworkDisconnected() {
    // reset the joined state, its server side is gone
    _joined = false;
    _joinedLobbyId = 0;
    _readyButton.SetPressedNoSignal(false);
    _statusLabel.Text = "Disconnected from the server";
    UpdateButtons();
  }

  private void RefreshList() {
    var network = _network;

    // remember the selection to restore it after the rebuild
    var selectedId = SelectedLobby()?.Id;
    _lobbyList.Clear();

    foreach (var lobby in network.Lobbies.OrderBy(lobby => lobby.Id)) {
      var text = $"Lobby {lobby.Id} — {lobby.PlayersCount}/{lobby.MaxPlayers} players, {lobby.ReadyCount} ready";
      if (lobby.Starting) {
        text += " (starting)";
      }

      var index = _lobbyList.AddItem(text);
      _lobbyList.SetItemMetadata(index, lobby.Id);

      if (lobby.Id == selectedId) {
        _lobbyList.Select(index);
      }
    }
  }

  private LobbyData? SelectedLobby() {
    var selected = _lobbyList.GetSelectedItems();
    if (selected.Length == 0) {
      return null;
    }

    var id = _lobbyList.GetItemMetadata(selected[0]).AsUInt64();
    return _network.Lobbies.FirstOrDefault(lobby => lobby.Id == id);
  }

  private void UpdateButtons() {
    _createButton.Disabled = _joined;
    _joinButton.Disabled = _joined || SelectedLobby() == null;
    _leaveButton.Disabled = !_joined;
    _readyButton.Disabled = !_joined;
  }

  private void OnCreatePressed() =>
    _network.CreateLobby(DEFAULT_MAX_PLAYERS, (uint)_tribeButton.GetSelectedId());

  private void OnJoinPressed() {
    var lobby = SelectedLobby();
    if (lobby == null) {
      return;
    }

    _network.JoinLobby(lobby.Id, (uint)_tribeButton.GetSelectedId());
  }

  private void OnLeavePressed() => _network.LeaveLobby(_joinedLobbyId);

  private void OnReadyToggled(bool ready) {
    if (_joined) {
      _network.SetReady(_joinedLobbyId, ready);
    }
  }

  private void OnLobbyCreated(LobbyActionResult result, ulong lobbyId) {
    if (result != LobbyActionResult.Ok) {
      ShowError(result);
      return;
    }

    SetJoined(lobbyId);
  }

  private void OnLobbyJoined(LobbyActionResult result, ulong lobbyId) {
    if (result != LobbyActionResult.Ok) {
      ShowError(result);
      return;
    }

    SetJoined(lobbyId);
  }

  private void OnLobbyLeft(LobbyActionResult result, ulong lobbyId) {
    if (result != LobbyActionResult.Ok) {
      ShowError(result);
      return;
    }

    _joined = false;
    _joinedLobbyId = 0;
    _readyButton.SetPressedNoSignal(false);
    _statusLabel.Text = "";
    UpdateButtons();
  }

  private void OnReadySet(LobbyActionResult result, ulong lobbyId) {
    if (result != LobbyActionResult.Ok) {
      ShowError(result);
      _readyButton.SetPressedNoSignal(false);
    }
  }

  private void OnGameStarted(GameStartedPacket packet) {
    if (packet.LobbyId != _joinedLobbyId) {
      return;
    }

    // TODO: switch to the game scene once the game itself is implemented
    _statusLabel.Text = "Game started!";
    GD.Print($"Game started for lobby {packet.LobbyId} with {packet.Players.Count} players");
  }

  private void SetJoined(ulong lobbyId) {
    _joined = true;
    _joinedLobbyId = lobbyId;
    _statusLabel.Text = $"In lobby {lobbyId}";
    UpdateButtons();
  }

  private void ShowError(LobbyActionResult result) => _statusLabel.Text = $"Error: {result}";
}
