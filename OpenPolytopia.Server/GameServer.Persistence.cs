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

  private string CaptureState() => JsonSerializer.Serialize(new SavedServer(2, _lobbyManager.LastId,
    [.. _lobbyManager.Lobbies], [.. _gameManager.Sessions.Select(session => new SavedSession(session.Id,
      session.Game.ToSnapshot(), new(session.Accounts), new(session.Names), session.Clock?.ToSnapshot()))]), _json);

  private void RestoreState(string? json) {
    if (json == null) return;
    var state = JsonSerializer.Deserialize<SavedServer>(json, _json) ?? throw new InvalidDataException("Empty server state");
    if (state.Version != 2) throw new InvalidDataException("Unsupported server state version");
    var nextLobbies = new LobbyManager();
    nextLobbies.Restore(state.NextLobbyId, state.Lobbies);
    var nextGames = new GameManager();
    foreach (var saved in state.Games) {
      var troops = new TroopManager(saved.Game.GridSize);
      troops.RegisterTroops(_gameData.TroopsSerializedData);
      var game = Game.Restore(saved.Game, troops, _gameData.Tribes, _gameData.Buildings, _gameData.TechTreeDefinition);
      var session = new GameSession(saved.Id, game, saved.Accounts, saved.Names) {
        Clock = TurnClock.FromSnapshot(saved.Clock ?? throw new InvalidDataException("Missing saved turn clock"))
      };
      foreach (var connection in session.ConnectionIds.ToArray()) session.RemoveConnection(connection);
      nextGames.Restore(session);
    }
    _lobbyManager = nextLobbies;
    _gameManager = nextGames;
    _savedState = json;
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
      var after = changes ? CaptureState() : before;
      if (after != before || _pendingRenames.Count != 0) _store.SaveState(after, _pendingRenames);
      _savedState = after;
      committed = true;
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
