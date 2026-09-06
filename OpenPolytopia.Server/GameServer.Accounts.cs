namespace OpenPolytopia.Server;

using OpenPolytopia.Common.Gameplay;
using OpenPolytopia.Common.Network;
using OpenPolytopia.Common.Network.Packets;

public partial class GameServer {
  private readonly Dictionary<uint, uint> _authenticated = new();
  private readonly Dictionary<uint, string> _sessionTokens = new();
  private readonly Dictionary<string, Queue<DateTimeOffset>> _loginAttempts = new();
  private readonly object _loginLock = new();
  private readonly SemaphoreSlim _passwordWorkers = new(2, 2);

  private async Task<(AuthResult? Result, bool Retryable)> CheckCredentialsAsync(NetworkConnection connection, IPacket packet) {
    await _stateLock.WaitAsync(_cts.Token);
    try {
      if (_authenticated.TryGetValue(connection.Id, out var id)) return (new AuthResult(new Account {
        Id = id, Username = "", DisplayName = _playerNames[connection.Id]
      }, _sessionTokens[connection.Id]), false);
    }
    finally { _stateLock.Release(); }
    var now = DateTimeOffset.UtcNow;
    lock (_loginLock) {
      foreach (var key in _loginAttempts.Keys.ToArray()) {
        var queue = _loginAttempts[key];
        while (queue.TryPeek(out var time) && now - time >= TimeSpan.FromMinutes(1)) queue.Dequeue();
        if (queue.Count == 0) _loginAttempts.Remove(key);
      }
      var keyForPeer = connection.RemoteAddress;
      if (!_loginAttempts.TryGetValue(keyForPeer, out var attempts)) {
        if (_loginAttempts.Count >= 4096) return (null, true);
        _loginAttempts[keyForPeer] = attempts = new();
      }
      if (attempts.Count >= 60) return (null, true);
      attempts.Enqueue(now);
    }
    // Token resume is cheap and does not compete for password workers.
    if (packet is ResumeSessionPacket resume) return (_store.Resume(resume.Token) is { } account
      ? new AuthResult(account, resume.Token) : null, false);
    if (!await _passwordWorkers.WaitAsync(0)) return (null, true);
    try {
      var result = await Task.Run(() => packet switch {
        RegisterAccountPacket register => _store.Register(register.Username, register.Password),
        LoginPacket login => _store.Login(login.Username, login.Password),
        _ => null
      });
      return (result, false);
    }
    catch (ArgumentException) { return (null, false); }
    finally { _passwordWorkers.Release(); }
  }

  private uint AccountId(NetworkConnection connection) => _authenticated.GetValueOrDefault(connection.Id);

  private void RegisterAccountHandlers() {
    _dispatcher.Register<LogoutPacket>((c, _) => {
      if (_sessionTokens.Remove(c.Id, out var token)) _store.Logout(token);
      _authenticated.Remove(c.Id);
      _playerNames.Remove(c.Id);
      _gameManager.Disconnect(c.Id);
      SendTo(c.Id, new AuthenticationPacket());
    });
    _dispatcher.Register<GetMyGamesPacket>((c, _) => SendTo(c.Id,
      new MyGamesPacket { GameIds = [.. _gameManager.FindByAccount(AccountId(c)).Select(s => s.Id).Concat(_store.CompletedGameIds(AccountId(c))).Distinct()] }));
    _dispatcher.Register<JoinGamePacket>((c, p) => {
      var session = FindSession(p.GameId, AccountId(c));
      if (session != null && session.Join(AccountId(c), c.Id)) {
        SendTo(c.Id, session.BuildState());
        SendClock(session, [c.Id]);
      }
      else SendTo(c.Id, new GameStatePacket { GameId = p.GameId,
        Result = session == null ? GameActionResult.GameNotFound : GameActionResult.NotInGame });
    });
    _dispatcher.Register<LeaveGamePacket>((c, p) => {
      var session = FindSession(p.GameId, AccountId(c));
      var result = session == null ? GameActionResult.GameNotFound :
        session.PlayerIdOfAccount(AccountId(c)) == 0 ? GameActionResult.NotInGame : GameActionResult.Ok;
      if (result == GameActionResult.Ok) session!.RemoveConnection(c.Id);
      SendTo(c.Id, new MembershipResultPacket { GameId = p.GameId, Result = result });
    });
    _dispatcher.Register<ResignGamePacket>((c, p) => {
      var session = FindSession(p.GameId, AccountId(c));
      var playerId = session?.PlayerIdOfAccount(AccountId(c)) ?? 0;
      var result = session == null ? GameActionResult.GameNotFound : playerId == 0 ? GameActionResult.NotInGame :
        session.Game.Resign(playerId).Result;
      SendTo(c.Id, new MembershipResultPacket { GameId = p.GameId, Result = result });
      if (result != GameActionResult.Ok) return;
      BroadcastTo(session!.ConnectionIds, new PlayerEliminatedPacket {
        GameId = session.Id, PlayerId = (uint)playerId, Update = session.TakeUpdate()
      });
      if (session.Game.Over) EndGame(session);
      else BroadcastTo(session.ConnectionIds, session.BuildState());
    });
  }

  private void Authenticate(NetworkConnection connection, Func<AuthResult?> authenticate, bool retryable = false) {
    if (_authenticated.TryGetValue(connection.Id, out var existing)) {
      SendTo(connection.Id, new AuthenticationPacket { Ok = true, PlayerId = existing,
        Name = _playerNames[connection.Id], Token = _sessionTokens[connection.Id] });
      return;
    }
    AuthResult? result;
    try { result = authenticate(); }
    catch (ArgumentException) { result = null; }
    if (result == null) {
      SendTo(connection.Id, new AuthenticationPacket { Retryable = retryable });
      return;
    }

    // One active transport per account prevents competing devices from racing the same seat.
    foreach (var previous in _authenticated.Where(pair => pair.Value == result.Account.Id).Select(pair => pair.Key).ToArray()) {
      _authenticated.Remove(previous);
      _playerNames.Remove(previous);
      _sessionTokens.Remove(previous);
      _gameManager.Disconnect(previous);
      Kick(previous);
    }
    _authenticated[connection.Id] = result.Account.Id;
    _playerNames[connection.Id] = result.Account.DisplayName;
    _sessionTokens[connection.Id] = result.Token;
    SendTo(connection.Id, new AuthenticationPacket {
      Ok = true, PlayerId = result.Account.Id, Name = result.Account.DisplayName, Token = result.Token
    });
  }
}
