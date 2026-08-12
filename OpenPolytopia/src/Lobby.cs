namespace OpenPolytopia;

using System;
using System.Collections.Specialized;
using System.Linq;
using Common;
using Common.Network.Packets;
using Godot;

/// <summary>
/// Lobby browser: lists the lobbies on the server and lets the player
/// create, join, leave and ready up in a lobby
/// </summary>
public partial class Lobby : Control {
  private const uint DEFAULT_MAX_PLAYERS = 4;

  private ItemList _lobbyList = null!;
  private OptionButton _tribeButton = null!;
  private Button _createButton = null!;
  private Button _joinButton = null!;
  private Button _leaveButton = null!;
  private CheckButton _readyButton = null!;
  private Label _statusLabel = null!;

  private ulong _joinedLobbyId;
  private bool _joined;

  public override void _Ready() {
    BuildUi();

    var network = NetworkNode.Instance;
    network.Lobbies.CollectionChanged += OnLobbiesChanged;
    network.OnLobbyCreated += OnLobbyCreated;
    network.OnLobbyJoined += OnLobbyJoined;
    network.OnLobbyLeft += OnLobbyLeft;
    network.OnReadySet += OnReadySet;
    network.OnGameStarted += OnGameStarted;

    network.RefreshLobbies();
    RefreshList();
    UpdateButtons();
  }

  public override void _ExitTree() {
    base._ExitTree();

    var network = NetworkNode.Instance;
    network.Lobbies.CollectionChanged -= OnLobbiesChanged;
    network.OnLobbyCreated -= OnLobbyCreated;
    network.OnLobbyJoined -= OnLobbyJoined;
    network.OnLobbyLeft -= OnLobbyLeft;
    network.OnReadySet -= OnReadySet;
    network.OnGameStarted -= OnGameStarted;
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
    refreshButton.Pressed += () => NetworkNode.Instance.RefreshLobbies();
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

  private void OnLobbiesChanged(object? sender, NotifyCollectionChangedEventArgs e) {
    RefreshList();
    UpdateButtons();
  }

  private void RefreshList() {
    var network = NetworkNode.Instance;
    _lobbyList.Clear();

    foreach (var lobby in network.Lobbies) {
      var text = $"Lobby {lobby.Id} — {lobby.PlayersCount}/{lobby.MaxPlayers} players, {lobby.ReadyCount} ready";
      if (lobby.Starting) {
        text += " (starting)";
      }

      var index = _lobbyList.AddItem(text);
      _lobbyList.SetItemMetadata(index, lobby.Id);
    }
  }

  private LobbyData? SelectedLobby() {
    var selected = _lobbyList.GetSelectedItems();
    if (selected.Length == 0) {
      return null;
    }

    var id = _lobbyList.GetItemMetadata(selected[0]).AsUInt64();
    return NetworkNode.Instance.Lobbies.FirstOrDefault(lobby => lobby.Id == id);
  }

  private void UpdateButtons() {
    _createButton.Disabled = _joined;
    _joinButton.Disabled = _joined || SelectedLobby() == null;
    _leaveButton.Disabled = !_joined;
    _readyButton.Disabled = !_joined;
  }

  private void OnCreatePressed() =>
    NetworkNode.Instance.CreateLobby(DEFAULT_MAX_PLAYERS, (uint)_tribeButton.GetSelectedId());

  private void OnJoinPressed() {
    var lobby = SelectedLobby();
    if (lobby == null) {
      return;
    }

    NetworkNode.Instance.JoinLobby(lobby.Id, (uint)_tribeButton.GetSelectedId());
  }

  private void OnLeavePressed() => NetworkNode.Instance.LeaveLobby(_joinedLobbyId);

  private void OnReadyToggled(bool ready) {
    if (_joined) {
      NetworkNode.Instance.SetReady(_joinedLobbyId, ready);
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
