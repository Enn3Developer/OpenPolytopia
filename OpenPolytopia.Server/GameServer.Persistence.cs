namespace OpenPolytopia.Server;

using System.Text.Json;
using OpenPolytopia.Common.Network;
using OpenPolytopia.Common;
using OpenPolytopia.Common.Gameplay;
using OpenPolytopia.Common.Network.Packets;

public partial class GameServer {
  private readonly ServerStore _store = new(databasePath ??
    Environment.GetEnvironmentVariable("OPENPOLYTOPIA_DATABASE") ?? "openpolytopia.db");
  private static readonly JsonSerializerOptions _json = new() { IncludeFields = true };
  private readonly List<Action> _outbox = [];
  private string? _savedState;
  private readonly Dictionary<uint, string> _pendingRenames = new();

  private sealed record SavedSession(ulong Id, GameSnapshot Game, Dictionary<int, uint> Accounts,
    Dictionary<int, string> Names, TurnClockState? Clock);
  private sealed record SavedServer(int Version, ulong NextLobbyId, List<LobbyData> Lobbies,
    List<SavedSession> Games);

  private static SavedSession SaveSession(GameSession session) => new(session.Id,
    session.Game.ToSnapshot(), new(session.Accounts), new(session.Names), session.Clock?.ToSnapshot());

  private string CaptureState(bool includeCompleted = true) => JsonSerializer.Serialize(new SavedServer(2, _lobbyManager.LastId,
    [.. _lobbyManager.Lobbies], [.. _gameManager.Sessions.Where(session => includeCompleted || !session.Game.Over).Select(SaveSession)]), _json);

  private void RestoreState(string? json) {
    if (json == null) return;
    var state = JsonSerializer.Deserialize<SavedServer>(json, _json) ?? throw new InvalidDataException("Empty server state");
    if (state.Version is not (1 or 2)) throw new InvalidDataException("Unsupported server state version");
    var nextLobbies = new LobbyManager();
    nextLobbies.Restore(state.NextLobbyId, state.Lobbies);
    var nextGames = new GameManager();
    foreach (var saved in state.Games) {
      var session = RestoreSession(saved, state.Version == 1);
      nextGames.Restore(session);
    }
    _lobbyManager = nextLobbies;
    _gameManager = nextGames;
    _savedState = state.Version == 1 ? CaptureState() : json;
  }

  private GameSession RestoreSession(SavedSession saved, bool legacy = false) {
    var troops = new TroopManager(saved.Game.GridSize);
    troops.RegisterTroops(_gameData.TroopsSerializedData);
    var game = Game.Restore(saved.Game, troops, _gameData.Tribes, _gameData.Buildings, _gameData.TechTreeDefinition);
    var session = new GameSession(saved.Id, game, saved.Accounts, saved.Names);
    if (saved.Clock != null) session.Clock = TurnClock.FromSnapshot(saved.Clock);
    else if (!game.Over) {
      if (!legacy) throw new InvalidDataException("Missing saved turn clock");
      // Pre-timer saves did not retain the chosen mode. Allow a full day after migration.
      session.Clock = new TurnClock(TurnTimerMode.Daily, game.Players.Select(player => player.Id));
      session.Clock.Begin(game.CurrentPlayer, _timeProvider.GetUtcNow());
    }
    foreach (var connection in session.ConnectionIds.ToArray()) session.RemoveConnection(connection);
    return session;
  }

  private GameSession? FindSession(ulong id, uint accountId) {
    if (_gameManager[id] is { } active) return active;
    var json = _store.LoadCompletedGame(id, accountId);
    if (json == null) return null;
    var saved = JsonSerializer.Deserialize<SavedSession>(json, _json) ?? throw new InvalidDataException("Empty completed game");
    var session = RestoreSession(saved);
    if (!session.Game.Over) throw new InvalidDataException("Archived game is still active");
    return session;
  }

  // A successful reply must never describe state which has not reached SQLite.
  private async Task WithStateAsync(Func<Task> action, bool stateMayChange = true) {
    if (_cts.IsCancellationRequested) return;
    await _stateLock.WaitAsync();
    if (_cts.IsCancellationRequested) { _stateLock.Release(); return; }
    string? before = null;
    var committed = false;
    var names = new Dictionary<uint, string>(_playerNames);
    var accounts = new Dictionary<uint, uint>(_authenticated);
    var tokens = new Dictionary<uint, string>(_sessionTokens);
    var attachments = _gameManager.Sessions.Where(s => s.Connections.Count != 0).ToDictionary(s => s.Id, s => s.Connections.ToArray());
    try {
      before = _savedState ??= CaptureState();
      var now = _timeProvider.GetUtcNow();
      var changes = stateMayChange || _lobbyManager.Lobbies.Any(l => l.Starting) ||
        _gameManager.Sessions.Any(s => !s.Game.Over && s.Clock?.Mode == TurnTimerMode.Live && s.Clock.IsExpired(now));
      ProcessTimers(now);
      await action();
      SynchronizeClocks(_timeProvider.GetUtcNow());
      var completed = _gameManager.Sessions.Where(session => session.Game.Over).Select(session =>
        new ArchivedGame(session.Id, JsonSerializer.Serialize(SaveSession(session), _json), session.Accounts.Values.ToArray())).ToArray();
      var after = changes || completed.Length != 0 ? CaptureState(false) : before;
      if (after != before || _pendingRenames.Count != 0 || completed.Length != 0)
        _store.SaveState(after, _pendingRenames, completed);
      _savedState = after;
      committed = true;
      foreach (var game in completed) _gameManager.RemoveGame(game.Id);
      foreach (var send in _outbox) send();
    }
    catch {
      if (committed) throw;
      try { RestoreState(before); }
      catch { Stop(); throw; }
      _authenticated.Clear();
      foreach (var (id, account) in accounts) _authenticated[id] = account;
      _sessionTokens.Clear();
      foreach (var (id, token) in tokens) _sessionTokens[id] = token;
      _playerNames.Clear();
      foreach (var (id, name) in names) _playerNames[id] = name;
      foreach (var (id, connections) in attachments) {
        var session = _gameManager[id]!;
        foreach (var (playerId, connectionId) in connections) session.Join(session.Accounts[playerId], connectionId);
      }
      throw;
    }
    finally {
      _outbox.Clear();
      _pendingRenames.Clear();
      _stateLock.Release();
    }
  }

  private void Kick(uint connection) => _outbox.Add(() => _server.Kick(connection));

  private void SendTo(uint connection, IPacket packet) {
    var frame = PacketProtocol.FramePacket(packet);
    _outbox.Add(() => _server.SendTo(connection, frame));
  }
  private void Broadcast(IPacket packet) => BroadcastTo(_authenticated.Keys, packet);
  private void BroadcastTo(IEnumerable<uint> connections, IPacket packet) {
    var recipients = connections.ToArray();
    var frame = PacketProtocol.FramePacket(packet);
    _outbox.Add(() => { foreach (var recipient in recipients) _server.SendTo(recipient, frame); });
  }
}
