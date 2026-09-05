namespace OpenPolytopia;

using System;
using System.Linq;
using Common;
using Common.Gameplay;
using Common.Network.Packets;
using Godot;

/// <summary>
/// Lists the lobbies on the server and the games this account takes part in
/// </summary>
/// <remarks>
/// Every lobby action works on the lobby selected in the list: an account can sit in several lobbies at once,
/// so there is no single "joined" lobby to act on
/// </remarks>
public partial class Lobby : Control {
  private const uint DEFAULT_MAX_PLAYERS = 4;

  // ids of the turn timers, mirroring the ones the server uses
  private const int TIMER_LIVE = 0;
  private const int TIMER_DAILY = 1;

  private ItemList _lobbyList = null!;
  private ItemList _gameList = null!;
  private OptionButton _tribeButton = null!;
  private OptionButton _timerButton = null!;
  private Button _createButton = null!;
  private Button _joinButton = null!;
  private Button _leaveButton = null!;
  private CheckButton _readyButton = null!;
  private Button _openButton = null!;
  private Button _closeButton = null!;
  private Button _resignButton = null!;
  private ConfirmationDialog _resignDialog = null!;
  private Label _statusLabel = null!;
  private Label _gameStatusLabel = null!;
  private Label _clockLabel = null!;

  private NetworkNode _network = null!;
  private ulong _openGameId;
  private ulong _requestedGameId;
  private ulong _resignGameId;

  // the countdown of the open game, measured against the local monotonic clock so a wrong system time can't skew it
  private bool _hasClock;
  private uint _clockPlayerId;
  private uint _myGamePlayerId;
  private long _clockRemainingMs;
  private ulong _clockReceivedAt;

  public override void _Ready() {
    BuildUi();

    // grab the instance once, the node could leave the tree before this scene
    _network = NetworkNode.Instance!;
    var network = _network;

    network.OnLobbiesChanged += OnLobbiesChanged;
    network.OnMyGamesChanged += OnMyGamesChanged;
    network.OnLobbyCreated += OnLobbyCreated;
    network.OnLobbyJoined += OnLobbyJoined;
    network.OnLobbyLeft += OnLobbyLeft;
    network.OnReadySet += OnReadySet;
    network.OnGameStarted += OnGameStarted;
    network.OnGameState += OnGameState;
    network.OnGameChanged += OnGameChanged;
    network.OnGameClock += OnGameClock;
    network.OnMembershipResult += OnMembershipResult;
    network.OnAuthenticated += OnAuthenticated;
    network.OnDisconnected += OnNetworkDisconnected;

    network.RefreshLobbies();
    network.RefreshMyGames();
    RefreshList();
    RefreshGameList();
    UpdateButtons();
  }

  public override void _ExitTree() {
    base._ExitTree();

    var network = _network;
    network.OnLobbiesChanged -= OnLobbiesChanged;
    network.OnMyGamesChanged -= OnMyGamesChanged;
    network.OnLobbyCreated -= OnLobbyCreated;
    network.OnLobbyJoined -= OnLobbyJoined;
    network.OnLobbyLeft -= OnLobbyLeft;
    network.OnReadySet -= OnReadySet;
    network.OnGameStarted -= OnGameStarted;
    network.OnGameState -= OnGameState;
    network.OnGameChanged -= OnGameChanged;
    network.OnGameClock -= OnGameClock;
    network.OnMembershipResult -= OnMembershipResult;
    network.OnAuthenticated -= OnAuthenticated;
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

    _timerButton = new OptionButton();
    _timerButton.AddItem("Live", TIMER_LIVE);
    _timerButton.AddItem("24h", TIMER_DAILY);

    // daily is the default, a live game needs everyone at the keyboard at the same time
    _timerButton.Select(_timerButton.GetItemIndex(TIMER_DAILY));
    topBar.AddChild(_timerButton);

    _createButton = new Button { Text = "Create lobby" };
    _createButton.Pressed += OnCreatePressed;
    topBar.AddChild(_createButton);

    var refreshButton = new Button { Text = "Refresh" };
    refreshButton.Pressed += OnRefreshPressed;
    topBar.AddChild(refreshButton);

    // without this the saved session would be the only account this machine can ever play as
    var logoutButton = new Button { Text = "Logout" };
    logoutButton.Pressed += OnLogoutPressed;
    topBar.AddChild(logoutButton);

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

    var gamesTitle = new Label { Text = "My games", HorizontalAlignment = HorizontalAlignment.Center };
    gamesTitle.AddThemeFontSizeOverride("font_size", 24);
    root.AddChild(gamesTitle);

    _gameList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
    _gameList.ItemSelected += _ => UpdateButtons();
    root.AddChild(_gameList);

    var gamesBar = new HBoxContainer();
    root.AddChild(gamesBar);

    _openButton = new Button { Text = "Open" };
    _openButton.Pressed += OnOpenPressed;
    gamesBar.AddChild(_openButton);

    _closeButton = new Button { Text = "Close" };
    _closeButton.Pressed += OnClosePressed;
    gamesBar.AddChild(_closeButton);

    _resignButton = new Button { Text = "Resign" };
    _resignButton.Pressed += OnResignPressed;
    gamesBar.AddChild(_resignButton);

    _gameStatusLabel = new Label();
    gamesBar.AddChild(_gameStatusLabel);

    _clockLabel = new Label();
    gamesBar.AddChild(_clockLabel);

    // resigning eliminates the player for good, make him say it twice
    _resignDialog = new ConfirmationDialog { Title = "Resign", OkButtonText = "Resign" };
    _resignDialog.Confirmed += OnResignConfirmed;
    AddChild(_resignDialog);
  }

  private void OnLobbiesChanged() {
    RefreshList();
    UpdateButtons();
  }

  private void OnMyGamesChanged() {
    RefreshGameList();
    UpdateButtons();
  }

  private void OnAuthenticated(bool ok) {
    if (!ok) {
      if (!_network.AuthenticationRetryable) GetTree().ChangeSceneToFile("res://src/Game.tscn");
      return;
    }

    // the server dropped every view of this account while the client was away
    _openGameId = 0;
    ClearClock();
    _statusLabel.Text = "";
    _gameStatusLabel.Text = "";
    UpdateButtons();
  }

  private void OnNetworkDisconnected() {
    // the memberships are still there server side, but this connection has no view of them anymore
    _openGameId = 0;
    ClearClock();
    _readyButton.SetPressedNoSignal(false);
    _statusLabel.Text = "Disconnected from the server";
    _gameStatusLabel.Text = "";
    UpdateButtons();
  }

  /// <summary>
  /// Stops the countdown, there is no open game to count down for anymore
  /// </summary>
  private void ClearClock() {
    _hasClock = false;
    _clockLabel.Text = "";
  }

  private void OnLogoutPressed() {
    _network.Logout();
    GetTree().ChangeSceneToFile("res://src/Game.tscn");
  }

  private void OnRefreshPressed() {
    _network.RefreshLobbies();
    _network.RefreshMyGames();
  }

  private void RefreshList() {
    var network = _network;

    // remember the selection to restore it after the rebuild
    var selectedId = SelectedLobby()?.Id;
    _lobbyList.Clear();

    foreach (var lobby in network.Lobbies.OrderBy(lobby => lobby.Id)) {
      var timer = lobby.TimerMode == TIMER_LIVE ? "live" : "24h";
      var text =
        $"Lobby {lobby.Id} — {lobby.PlayersCount}/{lobby.MaxPlayers} players, {lobby.ReadyCount} ready, {timer}";
      if (lobby.Starting) {
        text += " (starting)";
      }

      if (lobby[network.PlayerId] != null) {
        text += " (joined)";
      }

      var index = _lobbyList.AddItem(text);
      _lobbyList.SetItemMetadata(index, lobby.Id);

      if (lobby.Id == selectedId) {
        _lobbyList.Select(index);
      }
    }
  }

  private void RefreshGameList() {
    var selectedId = SelectedGame();
    _gameList.Clear();

    foreach (var gameId in _network.MyGames.OrderBy(gameId => gameId)) {
      var text = $"Game {gameId}";
      if (gameId == _openGameId) {
        text += " (open)";
      }

      var index = _gameList.AddItem(text);
      _gameList.SetItemMetadata(index, gameId);

      if (gameId == selectedId) {
        _gameList.Select(index);
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

  /// <summary>
  /// Returns the id of the game selected in the list, or 0 when nothing is selected
  /// </summary>
  private ulong SelectedGame() {
    var selected = _gameList.GetSelectedItems();
    return selected.Length == 0 ? 0 : _gameList.GetItemMetadata(selected[0]).AsUInt64();
  }

  /// <summary>
  /// Returns this account inside a lobby, or null when it isn't part of it
  /// </summary>
  private LobbyPlayerData? MemberOf(LobbyData? lobby) => lobby?[_network.PlayerId];

  private void UpdateButtons() {
    var lobby = SelectedLobby();
    var member = MemberOf(lobby);

    _createButton.Disabled = !_network.Authenticated;
    _joinButton.Disabled = lobby == null || member != null;
    _leaveButton.Disabled = member == null;
    _readyButton.Disabled = member == null;
    _readyButton.SetPressedNoSignal(member?.Ready ?? false);

    var gameId = SelectedGame();
    _openButton.Disabled = gameId == 0 || gameId == _openGameId;
    _closeButton.Disabled = _openGameId == 0;
    _resignButton.Disabled = gameId == 0;
  }

  private void OnCreatePressed() =>
    _network.CreateLobby(DEFAULT_MAX_PLAYERS, (uint)_tribeButton.GetSelectedId(), (uint)_timerButton.GetSelectedId());

  private void OnJoinPressed() {
    var lobby = SelectedLobby();
    if (lobby == null) {
      return;
    }

    _network.JoinLobby(lobby.Id, (uint)_tribeButton.GetSelectedId());
  }

  private void OnLeavePressed() {
    var lobby = SelectedLobby();
    if (lobby == null) {
      return;
    }

    _network.LeaveLobby(lobby.Id);
  }

  private void OnReadyToggled(bool ready) {
    var lobby = SelectedLobby();
    if (MemberOf(lobby) == null) {
      return;
    }

    _network.SetReady(lobby!.Id, ready);
  }

  private void OnOpenPressed() {
    var gameId = SelectedGame();
    if (gameId == 0) {
      return;
    }

    _requestedGameId = gameId;
    _gameStatusLabel.Text = $"Opening game {gameId}...";
    _network.OpenGame(gameId);
  }

  private void OnClosePressed() {
    if (_openGameId == 0) {
      return;
    }

    _network.CloseGame(_openGameId);
  }

  private void OnResignPressed() {
    var gameId = SelectedGame();
    if (gameId == 0) {
      return;
    }

    _resignGameId = gameId;
    _resignDialog.DialogText = $"Resign from game {gameId}? This eliminates you and cannot be undone.";
    _resignDialog.PopupCentered();
  }

  private void OnResignConfirmed() {
    if (_resignGameId == 0) {
      return;
    }

    // the id stays set until the response arrives, it's what tells a resign from a close
    _network.ResignGame(_resignGameId);
  }

  private void OnLobbyCreated(LobbyActionResult result, ulong lobbyId) {
    if (result != LobbyActionResult.Ok) {
      ShowError(result);
      return;
    }

    _statusLabel.Text = $"Created lobby {lobbyId}";
    UpdateButtons();
  }

  private void OnLobbyJoined(LobbyActionResult result, ulong lobbyId) {
    if (result != LobbyActionResult.Ok) {
      ShowError(result);
      return;
    }

    _statusLabel.Text = $"Joined lobby {lobbyId}";
    UpdateButtons();
  }

  private void OnLobbyLeft(LobbyActionResult result, ulong lobbyId) {
    if (result != LobbyActionResult.Ok) {
      ShowError(result);
      return;
    }

    _statusLabel.Text = $"Left lobby {lobbyId}";
    UpdateButtons();
  }

  private void OnReadySet(LobbyActionResult result, ulong lobbyId) {
    if (result != LobbyActionResult.Ok) {
      ShowError(result);
    }

    // the server is the one holding the ready state, put the button back on whatever it says
    UpdateButtons();
  }

  private void OnGameStarted(GameStartedPacket packet) {
    if (_network.Lobbies.FirstOrDefault(lobby => lobby.Id == packet.LobbyId) is { } lobby &&
        MemberOf(lobby) == null) {
      return;
    }

    _statusLabel.Text = $"Game {packet.LobbyId} started!";
    GD.Print($"Game started for lobby {packet.LobbyId} with {packet.Players.Count} players");
  }

  private void OnGameState(GameStatePacket packet) {
    if (packet.GameId != _openGameId && packet.GameId != _requestedGameId) return;
    if (packet.GameId == _requestedGameId) _requestedGameId = 0;
    if (packet.Result != GameActionResult.Ok) {
      _gameStatusLabel.Text = $"Game {packet.GameId}: {packet.Result}";
      return;
    }

    // the countdown belonged to whatever was open before, the server sends a new one for this game
    if (_openGameId != packet.GameId || packet.Over) {
      ClearClock();
    }

    _openGameId = packet.GameId;

    // TODO: replace this with the game scene once the gameplay is implemented
    var me = packet.Players.FirstOrDefault(player => player.AccountId == _network.PlayerId);
    _myGamePlayerId = me?.PlayerId ?? 0;
    _gameStatusLabel.Text = packet.Over
      ? $"Game {packet.GameId}: over, player {packet.Winner} won"
      : $"Game {packet.GameId}: turn {packet.Turn}, player {packet.CurrentPlayer} plays" +
        (me == null ? "" : $", you have {me.Stars} stars and {me.Score} points");

    RefreshGameList();
    UpdateButtons();
  }

  /// <summary>
  /// Refreshes the countdown of the open game's turn
  /// </summary>
  /// <param name="delta">ignored</param>
  public override void _Process(double delta) {
    if (!_hasClock) {
      return;
    }

    var left = _clockRemainingMs - (long)(Time.GetTicksMsec() - _clockReceivedAt);
    var who = _clockPlayerId == _myGamePlayerId ? "You have" : $"Player {_clockPlayerId} has";
    _clockLabel.Text = left <= 0 ? "Turn overdue" : $"{who} {TimeSpan.FromMilliseconds(left):hh\\:mm\\:ss} left";
  }

  private void OnGameClock(GameClockPacket packet) {
    if (packet.GameId != _openGameId) {
      return;
    }

    // the deadline only means something next to the server clock it came with, the local one could be off by anything
    _clockRemainingMs = packet.DeadlineUnixMilliseconds - packet.ServerUnixMilliseconds;
    _clockReceivedAt = Time.GetTicksMsec();
    _clockPlayerId = packet.PlayerId;
    _hasClock = true;
  }

  private void OnGameChanged(ulong gameId) {
    // the client keeps no copy of the game, ask for the whole state again
    if (gameId == _openGameId) {
      _network.RequestGameState(gameId);
    }
  }

  private void OnMembershipResult(MembershipResultPacket packet) {
    // leaving and resigning share this response, only the client knows which one it asked for
    var resigned = packet.GameId == _resignGameId;
    _resignGameId = 0;

    if (packet.Result != GameActionResult.Ok) {
      _gameStatusLabel.Text = $"Game {packet.GameId}: {packet.Result}";
      return;
    }

    // resigning keeps the game open, the player can still watch it play out
    if (!resigned && packet.GameId == _openGameId) {
      _openGameId = 0;
      ClearClock();
    }

    _gameStatusLabel.Text = resigned ? $"Game {packet.GameId}: resigned" : $"Game {packet.GameId}: closed";
    RefreshGameList();
    UpdateButtons();
  }

  private void ShowError(LobbyActionResult result) => _statusLabel.Text = $"Error: {result}";
}
