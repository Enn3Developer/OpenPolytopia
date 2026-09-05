namespace OpenPolytopia.Server;

using OpenPolytopia.Common.Gameplay;
using OpenPolytopia.Common.Network;
using OpenPolytopia.Common.Network.Packets;

public partial class GameServer {
  private readonly Dictionary<uint, uint> _authenticated = new();
  private readonly Dictionary<uint, string> _sessionTokens = new();
  private readonly Queue<DateTimeOffset> _loginAttempts = new();

  private uint AccountId(NetworkConnection connection) => _authenticated.GetValueOrDefault(connection.Id);

  private void RegisterAccountHandlers() {
    _dispatcher.Register<RegisterAccountPacket>((c, p) => Authenticate(c, () => _store.Register(p.Username, p.Password)));
    _dispatcher.Register<LoginPacket>((c, p) => Authenticate(c, () => _store.Login(p.Username, p.Password)));
    _dispatcher.Register<ResumeSessionPacket>((c, p) => Authenticate(c, () =>
      _store.Resume(p.Token) is { } account ? new AuthResult(account, p.Token) : null));
    _dispatcher.Register<LogoutPacket>((c, _) => {
      if (_sessionTokens.Remove(c.Id, out var token)) _store.Logout(token);
      _authenticated.Remove(c.Id);
      _playerNames.Remove(c.Id);
      _gameManager.Disconnect(c.Id);
      SendTo(c.Id, new AuthenticationPacket());
    });
    _dispatcher.Register<GetMyGamesPacket>((c, _) => SendTo(c.Id,
      new MyGamesPacket { GameIds = [.. _gameManager.FindByAccount(AccountId(c)).Select(s => s.Id)] }));
    _dispatcher.Register<JoinGamePacket>((c, p) => {
      var session = _gameManager[p.GameId];
      SendTo(c.Id, session != null && session.Join(AccountId(c), c.Id) ? session.BuildState() :
        new GameStatePacket { GameId = p.GameId, Result = session == null ? GameActionResult.GameNotFound : GameActionResult.NotInGame });
    });
    _dispatcher.Register<LeaveGamePacket>((c, p) => {
      var session = _gameManager[p.GameId];
      var result = session == null ? GameActionResult.GameNotFound :
        session.PlayerIdOfAccount(AccountId(c)) == 0 ? GameActionResult.NotInGame : GameActionResult.Ok;
      if (result == GameActionResult.Ok) session!.RemoveConnection(c.Id);
      SendTo(c.Id, new MembershipResultPacket { GameId = p.GameId, Result = result });
    });
    _dispatcher.Register<ResignGamePacket>((c, p) => {
      var session = _gameManager[p.GameId];
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

  private void Authenticate(NetworkConnection connection, Func<AuthResult?> authenticate) {
    // Bound expensive password work globally, including across reconnects.
    var now = DateTimeOffset.UtcNow;
    while (_loginAttempts.TryPeek(out var time) && now - time >= TimeSpan.FromMinutes(1)) _loginAttempts.Dequeue();
    if (_authenticated.ContainsKey(connection.Id) || _loginAttempts.Count >= 120) {
      SendTo(connection.Id, new AuthenticationPacket());
      return;
    }
    _loginAttempts.Enqueue(now);
    AuthResult? result;
    try { result = authenticate(); }
    catch (ArgumentException) { result = null; }
    if (result == null) {
      SendTo(connection.Id, new AuthenticationPacket());
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
