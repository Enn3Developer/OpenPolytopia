namespace OpenPolytopia.Server;

using OpenPolytopia.Common.Gameplay;
using OpenPolytopia.Common.Network.Packets;

public partial class GameServer {
  private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

  private void RegisterTimerHandlers() {
    _dispatcher.Register<ResolveOverdueTurnPacket>((connection, packet) => {
      var session = FindSession(packet.GameId, AccountId(connection));
      var playerId = session?.PlayerIdOfAccount(AccountId(connection)) ?? 0;
      var now = _timeProvider.GetUtcNow();
      var result = session == null ? GameActionResult.GameNotFound :
        playerId == 0 ? GameActionResult.NotInGame :
        session.Game.Over ? GameActionResult.GameOver :
        !session.Game[playerId]!.Alive ? GameActionResult.PlayerEliminated :
        session.Game.CurrentPlayer == playerId || session.Game.Turn != packet.ExpectedTurn ||
        session.Game.CurrentPlayer != packet.ExpectedPlayer || session.Clock?.Mode != TurnTimerMode.Daily ||
        !session.Clock.IsExpired(now) ? GameActionResult.InvalidParameters : GameActionResult.Ok;
      if (result == GameActionResult.Ok) AdvanceExpired(session!, packet.Kick, now);
      SendTo(connection.Id, new MembershipResultPacket { GameId = packet.GameId, Result = result });
    });
  }

  private static (int Cities, int Units) ClockBonus(GameSession session, int playerId) =>
    ((int)session.Game.OwnedCities(playerId), session.Game.Troops.Troops().Count(t => t.Troop.Player == playerId));

  private void SendClock(GameSession session, IEnumerable<uint> recipients) {
    if (session.Clock?.DeadlineUtc is not { } deadline || session.Game.Over) return;
    BroadcastTo(recipients, new GameClockPacket {
      GameId = session.Id, TimerMode = (uint)session.Clock.Mode, PlayerId = (uint)session.Game.CurrentPlayer,
      DeadlineUnixMilliseconds = deadline.ToUnixTimeMilliseconds(),
      ServerUnixMilliseconds = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()
    });
  }

  // Apply expiry before accepting an action. A late EndTurn cannot erase a timeout.
  private void ProcessTimers(DateTimeOffset now) {
    foreach (var session in _gameManager.Sessions) {
      while (!session.Game.Over && session.Clock is { Mode: TurnTimerMode.Live } clock && clock.IsExpired(now)) {
        // Advance from the original deadline, so downtime cannot grant a new bank.
        AdvanceExpired(session, false, clock.DeadlineUtc!.Value);
      }
    }
  }

  private void AdvanceExpired(GameSession session, bool kick, DateTimeOffset at) {
    var clock = session.Clock!;
    var playerId = session.Game.CurrentPlayer;
    var (cities, units) = ClockBonus(session, playerId);
    var decision = kick ? clock.Kick(at) : clock.Skip(cities, units, at);
    var eliminated = decision == TurnClockDecision.Eliminated;
    var result = eliminated ? session.Game.Resign(playerId) : session.Game.EndTurn(playerId);
    if (result.Result != GameActionResult.Ok) throw new InvalidOperationException("Clock and game disagree on the current turn");
    if (eliminated) BroadcastTo(session.ConnectionIds, new PlayerEliminatedPacket {
      GameId = session.Id, PlayerId = (uint)playerId, Update = session.TakeUpdate()
    });
    if (session.Game.Over) {
      EndGame(session);
      return;
    }
    clock.Begin(session.Game.CurrentPlayer, at);
    BroadcastTo(session.ConnectionIds, new TurnStartedPacket {
      GameId = session.Id, Turn = session.Game.Turn, PlayerId = (uint)session.Game.CurrentPlayer,
      Update = session.TakeUpdate()
    });
    SendClock(session, session.ConnectionIds);
  }

  // EndTurn, resignation and capture can all change or end the active turn.
  private void SynchronizeClocks(DateTimeOffset now) {
    foreach (var session in _gameManager.Sessions) {
      var clock = session.Clock;
      if (clock == null || !clock.Running || (!session.Game.Over && clock.ActivePlayer == session.Game.CurrentPlayer)) continue;
      var (cities, units) = ClockBonus(session, clock.ActivePlayer);
      clock.Complete(cities, units, now);
      if (!session.Game.Over) {
        clock.Begin(session.Game.CurrentPlayer, now);
        SendClock(session, session.ConnectionIds);
      }
    }
  }
}
