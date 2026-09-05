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
  private readonly Dictionary<uint, string> _pendingRenames = new();

  private sealed record SavedSession(ulong Id, GameSnapshot Game, Dictionary<int, uint> Accounts,
    Dictionary<int, string> Names);
  private sealed record SavedServer(int Version, ulong NextLobbyId, List<LobbyData> Lobbies,
    List<SavedSession> Games);

  private string CaptureState() => JsonSerializer.Serialize(new SavedServer(1, _lobbyManager.LastId,
    [.. _lobbyManager.Lobbies], [.. _gameManager.Sessions.Select(session => new SavedSession(session.Id,
      session.Game.ToSnapshot(), new(session.Accounts), new(session.Names)))]), _json);

  private void RestoreState(string? json) {
    if (json == null) return;
    var state = JsonSerializer.Deserialize<SavedServer>(json, _json) ?? throw new InvalidDataException("Empty server state");
    if (state.Version != 1) throw new InvalidDataException("Unsupported server state version");
    _lobbyManager.Restore(state.NextLobbyId, state.Lobbies);
    _gameManager.Clear();
    foreach (var saved in state.Games) {
      var troops = new TroopManager(saved.Game.GridSize);
      troops.RegisterTroops(_gameData.TroopsSerializedData);
      var game = Game.Restore(saved.Game, troops, _gameData.Tribes, _gameData.Buildings, _gameData.TechTreeDefinition);
      var session = new GameSession(saved.Id, game, saved.Accounts, saved.Names);
      foreach (var connection in session.ConnectionIds.ToArray()) session.RemoveConnection(connection);
      _gameManager.Restore(session);
    }
  }

  // A successful reply must never describe state which has not reached SQLite.
  private async Task WithStateAsync(Func<Task> action) {
    await _stateLock.WaitAsync();
    string? before = null;
    var committed = false;
    var names = new Dictionary<uint, string>(_playerNames);
    var attachments = _gameManager.Sessions.ToDictionary(s => s.Id, s => s.Connections.ToArray());
    try {
      before = CaptureState();
      await action();
      var after = CaptureState();
      if (after != before || _pendingRenames.Count != 0) _store.SaveState(after, _pendingRenames);
      committed = true;
      foreach (var send in _outbox) send();
    }
    catch {
      if (committed) throw;
      RestoreState(before);
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
