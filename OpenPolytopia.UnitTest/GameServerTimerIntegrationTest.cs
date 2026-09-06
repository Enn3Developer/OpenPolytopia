namespace OpenPolytopia;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Common.Gameplay;
using Common.Network.Packets;
using Server;
using Shouldly;

/// <summary>
/// A <see cref="TimeProvider"/> that only moves when a test says so
/// </summary>
/// <remarks>
/// The server reads the clock from its packet handlers and from its background loop, so the instant has to be
/// readable from any thread; ticks are a single long, which <see cref="Interlocked"/> can publish atomically
/// </remarks>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider {
  private long _utcTicks = start.UtcTicks;

  public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

  /// <summary>Moves the clock forward; every deadline already handed out stays where it is</summary>
  public void Advance(TimeSpan delta) => Interlocked.Add(ref _utcTicks, delta.Ticks);

  /// <summary>The current instant, in the unit <see cref="GameClockPacket"/> uses</summary>
  public long UnixMilliseconds => GetUtcNow().ToUnixTimeMilliseconds();
}

/// <summary>
/// End to end tests of the turn timers, over a real loopback socket and a real SQLite database
/// </summary>
/// <remarks>
/// <para>
/// Time is the only thing faked here: the server gets a <see cref="ManualTimeProvider"/> nobody but the test moves,
/// so an expiry happens exactly when a test asks for it and never because a machine was slow. Everything else is
/// real, assertions included: they're made on the packets a client actually receives.
/// </para>
/// <para>
/// The server applies expired live timers at the top of every request and once per second from its background loop,
/// so after an <see cref="ManualTimeProvider.Advance"/> the tests wait for the packets instead of sleeping.
/// </para>
/// </remarks>
public class GameServerTimerIntegrationTest {
  /// <summary>A fixed instant, so a failure reports the same numbers on every machine</summary>
  private static readonly DateTimeOffset _epoch = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

  /// <summary><see cref="LobbyData.TimerMode"/> of a game whose turns are timed by a bank</summary>
  private const uint LIVE = (uint)TurnTimerMode.Live;

  /// <summary><see cref="LobbyData.TimerMode"/> of a game whose turns last 24 hours</summary>
  private const uint DAILY = (uint)TurnTimerMode.Daily;

  private static readonly TimeSpan INITIAL_BANK = TimeSpan.FromSeconds(TurnClock.INITIAL_BANK_SECONDS);

  /// <summary>Long enough to outlast a few ticks of the server's one second loop</summary>
  private static readonly TimeSpan QUIET = TimeSpan.FromSeconds(3);

  #region Live timers

  [Fact]
  public async Task TestLiveDeadlineSurvivesAReconnectAndARestart() {
    using var database = new TempDatabase();
    var time = new ManualTimeProvider(_epoch);
    var aliceName = TestGames.UniqueName("timer_alice");

    ulong gameId;
    string aliceToken;
    long deadline;

    await using (var first = await TestServer.StartAsync(database.Path, time)) {
      using var bob = await TestClient.ConnectAsync(first);
      await bob.RegisterAsync(TestGames.UniqueName("timer_bob"), TestGames.PASSWORD);

      // scoped, so the transport of player 1 is really gone before she comes back on a new one
      using (var alice = await TestClient.ConnectAsync(first)) {
        aliceToken = (await alice.RegisterAsync(aliceName, TestGames.PASSWORD)).Token;
        gameId = await TestGames.StartGameAsync(alice, bob, LIVE);

        // the first turn is timed by player 1's untouched bank, and the deadline is an absolute instant
        var started = await ClockAsync(alice, gameId, playerId: 1, notBefore: time.UnixMilliseconds);
        started.TimerMode.ShouldBe(LIVE);
        started.ServerUnixMilliseconds.ShouldBe(time.UnixMilliseconds);
        started.DeadlineUnixMilliseconds.ShouldBe(time.UnixMilliseconds + (long)INITIAL_BANK.TotalMilliseconds);
        deadline = started.DeadlineUnixMilliseconds;

        // a third of the bank is burned while the owner of the turn walks away
        time.Advance(TimeSpan.FromSeconds(20));
      }

      using var reconnected = await TestClient.ConnectAsync(first);
      (await reconnected.ResumeAsync(aliceToken)).Ok.ShouldBeTrue();
      await reconnected.SendAsync(new JoinGamePacket { GameId = gameId });
      (await reconnected.ExpectAsync<GameStatePacket>(packet => packet.Result == GameActionResult.Ok)).CurrentPlayer
        .ShouldBe(1u);

      // the very same deadline: being away doesn't refill the bank, and coming back doesn't either
      var resumed = await ClockAsync(reconnected, gameId, playerId: 1, notBefore: time.UnixMilliseconds);
      resumed.DeadlineUnixMilliseconds.ShouldBe(deadline);
      resumed.ServerUnixMilliseconds.ShouldBe(time.UnixMilliseconds);
      (resumed.DeadlineUnixMilliseconds - resumed.ServerUnixMilliseconds).ShouldBe(40_000);
    }

    // the server goes away for another 20 seconds of game time, which the bank has to pay for as well
    time.Advance(TimeSpan.FromSeconds(20));

    await using var second = await TestServer.StartAsync(database.Path, time);
    using var restarted = await TestClient.ConnectAsync(second);
    (await restarted.ResumeAsync(aliceToken)).Ok.ShouldBeTrue();
    await restarted.SendAsync(new JoinGamePacket { GameId = gameId });
    var state = await restarted.ExpectAsync<GameStatePacket>(packet => packet.Result == GameActionResult.Ok);
    state.CurrentPlayer.ShouldBe(1u);
    state.Over.ShouldBeFalse();

    var restored = await ClockAsync(restarted, gameId, playerId: 1, notBefore: time.UnixMilliseconds);
    restored.DeadlineUnixMilliseconds.ShouldBe(deadline);
    (restored.DeadlineUnixMilliseconds - restored.ServerUnixMilliseconds).ShouldBe(20_000);

    // and the restored deadline is still in the future, so nothing expires on its own
    (await restarted.TryReceiveAsync<TurnStartedPacket>(null, QUIET)).ShouldBeNull();
    (await restarted.GetStateAsync(gameId)).CurrentPlayer.ShouldBe(1u);
  }

  [Fact]
  public async Task TestALateEndTurnCannotUndoALiveTimeout() {
    using var database = new TempDatabase();
    var time = new ManualTimeProvider(_epoch);
    await using var server = await TestServer.StartAsync(database.Path, time);

    using var alice = await TestClient.ConnectAsync(server);
    await alice.RegisterAsync(TestGames.UniqueName("late_alice"), TestGames.PASSWORD);
    using var bob = await TestClient.ConnectAsync(server);
    await bob.RegisterAsync(TestGames.UniqueName("late_bob"), TestGames.PASSWORD);

    var gameId = await TestGames.StartGameAsync(alice, bob, LIVE);
    var before = await alice.GetStateAsync(gameId);
    before.CurrentPlayer.ShouldBe(1u);

    // one second past the deadline: whether the loop or the request itself notices, the turn is already gone
    time.Advance(INITIAL_BANK + TimeSpan.FromSeconds(1));

    // the round the packet names is still the one running, so it's the expired clock that refuses it and
    // nothing else
    await alice.SendAsync(new EndTurnPacket { GameId = gameId, ExpectedTurn = before.Turn });
    (await alice.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldBe(GameActionResult.NotYourTurn);

    var passed = await bob.ExpectAsync<TurnStartedPacket>(packet => packet.GameId == gameId);
    passed.PlayerId.ShouldBe(2u);
    passed.Turn.ShouldBe(before.Turn);

    // the authoritative state agrees: the late packet changed nothing at all
    var after = await alice.GetStateAsync(gameId);
    after.CurrentPlayer.ShouldBe(2u);
    after.Turn.ShouldBe(before.Turn);
    after.Over.ShouldBeFalse();
    after.Players.ShouldAllBe(player => player.Alive);

    // player 2 got a full bank, timed from the deadline player 1 blew and not from now
    var handed = await ClockAsync(bob, gameId, playerId: 2, notBefore: time.UnixMilliseconds);
    handed.DeadlineUnixMilliseconds.ShouldBe(
      _epoch.ToUnixTimeMilliseconds() + (long)(2 * INITIAL_BANK.TotalMilliseconds));

    // and the timeout stuck: when the turn comes back, player 1 only has the bonus of the turn she never played
    await bob.SendAsync(new EndTurnPacket { GameId = gameId, ExpectedTurn = after.Turn });
    (await bob.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldBe(GameActionResult.Ok);

    var punished = await ClockAsync(alice, gameId, playerId: 1, notBefore: time.UnixMilliseconds);
    var left = punished.DeadlineUnixMilliseconds - punished.ServerUnixMilliseconds;
    left.ShouldBeGreaterThan(0);
    left.ShouldBeLessThan((long)INITIAL_BANK.TotalMilliseconds);
  }

  [Fact]
  public async Task TestEndingATurnVoluntarilyBanksTheBonus() {
    using var database = new TempDatabase();
    var time = new ManualTimeProvider(_epoch);
    await using var server = await TestServer.StartAsync(database.Path, time);

    using var alice = await TestClient.ConnectAsync(server);
    await alice.RegisterAsync(TestGames.UniqueName("bank_alice"), TestGames.PASSWORD);
    using var bob = await TestClient.ConnectAsync(server);
    await bob.RegisterAsync(TestGames.UniqueName("bank_bob"), TestGames.PASSWORD);

    var gameId = await TestGames.StartGameAsync(alice, bob, LIVE);
    var before = await alice.GetStateAsync(gameId);

    // player 1 thinks for ten seconds and then ends her turn herself, well inside the deadline
    time.Advance(TimeSpan.FromSeconds(10));
    await alice.SendAsync(new EndTurnPacket { GameId = gameId, ExpectedTurn = before.Turn });
    (await alice.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldBe(GameActionResult.Ok);
    var passed = await bob.ExpectAsync<TurnStartedPacket>(packet => packet.GameId == gameId);
    passed.PlayerId.ShouldBe(2u);

    // player 2's bank was frozen while it wasn't his turn
    var handed = await ClockAsync(bob, gameId, playerId: 2, notBefore: time.UnixMilliseconds);
    (handed.DeadlineUnixMilliseconds - handed.ServerUnixMilliseconds).ShouldBe((long)INITIAL_BANK.TotalMilliseconds);

    time.Advance(TimeSpan.FromSeconds(5));
    await bob.SendAsync(new EndTurnPacket { GameId = gameId, ExpectedTurn = passed.Turn });
    (await bob.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldBe(GameActionResult.Ok);

    // player 1 gets back what she didn't spend plus the bonus of a turn played, so more than she started with
    var refilled = await ClockAsync(alice, gameId, playerId: 1, notBefore: time.UnixMilliseconds);
    var left = refilled.DeadlineUnixMilliseconds - refilled.ServerUnixMilliseconds;
    var unspent = (long)INITIAL_BANK.TotalMilliseconds - 10_000;
    // every player starts with a capital and the warrior spawned on it, so the bonus is at least this much
    var minimumBonus = (long)(TurnClock.TURN_BONUS_SECONDS + TurnClock.CITY_BONUS_SECONDS +
      TurnClock.UNIT_BONUS_SECONDS) * 1000;
    left.ShouldBeGreaterThanOrEqualTo(unspent + minimumBonus);
    left.ShouldBeGreaterThan((long)INITIAL_BANK.TotalMilliseconds);
  }

  [Fact]
  public async Task TestThreeLiveTimeoutsEliminateThePlayer() {
    using var database = new TempDatabase();
    var time = new ManualTimeProvider(_epoch);
    await using var server = await TestServer.StartAsync(database.Path, time);

    using var alice = await TestClient.ConnectAsync(server);
    await alice.RegisterAsync(TestGames.UniqueName("afk_alice"), TestGames.PASSWORD);
    using var bob = await TestClient.ConnectAsync(server);
    await bob.RegisterAsync(TestGames.UniqueName("afk_bob"), TestGames.PASSWORD);

    var gameId = await TestGames.StartGameAsync(alice, bob, LIVE);

    // one blown deadline costs a turn, not the game
    time.Advance(INITIAL_BANK);
    (await bob.ExpectAsync<TurnStartedPacket>(packet => packet.GameId == gameId)).PlayerId.ShouldBe(2u);
    (await bob.TryReceiveAsync<PlayerEliminatedPacket>(null, QUIET)).ShouldBeNull();
    (await bob.GetStateAsync(gameId)).Players.ShouldAllBe(player => player.Alive);

    // nobody plays for two hours: the server catches up on the deadlines it slept through, one turn at a time,
    // and every one of them costs its owner another timeout
    time.Advance(TimeSpan.FromHours(2));

    // exactly three more turns change hands: the second timeout of each player and the third of player 1, which
    // is the one that ends her game. A single advance, or one keyed off the wall clock, would produce fewer
    foreach (var expected in new[] { 1u, 2u, 1u }) {
      (await bob.ExpectAsync<TurnStartedPacket>(packet => packet.GameId == gameId)).PlayerId.ShouldBe(expected);
    }

    var eliminated = await bob.ExpectAsync<PlayerEliminatedPacket>(packet => packet.GameId == gameId);
    eliminated.PlayerId.ShouldBe(1u);

    var over = await bob.ExpectAsync<GameOverPacket>(packet => packet.GameId == gameId);
    over.Winner.ShouldBe(2u);
    over.Players.Single(player => player.PlayerId == 1).Alive.ShouldBeFalse();
    over.Players.Single(player => player.PlayerId == 2).Alive.ShouldBeTrue();

    // the catch-up stops at the elimination instead of running on to the wall clock
    (await bob.TryReceiveAsync<TurnStartedPacket>(null, QUIET)).ShouldBeNull();

    var final = await alice.GetStateAsync(gameId);
    final.Over.ShouldBeTrue();
    final.Winner.ShouldBe(2u);
  }

  #endregion

  #region Daily timers

  [Fact]
  public async Task TestAnOverdueDailyTurnOnlyMovesWhenAnEligibleMemberAsks() {
    using var database = new TempDatabase();
    var time = new ManualTimeProvider(_epoch);
    await using var server = await TestServer.StartAsync(database.Path, time);

    using var alice = await TestClient.ConnectAsync(server);
    await alice.RegisterAsync(TestGames.UniqueName("daily_alice"), TestGames.PASSWORD);
    using var bob = await TestClient.ConnectAsync(server);
    await bob.RegisterAsync(TestGames.UniqueName("daily_bob"), TestGames.PASSWORD);
    using var carol = await TestClient.ConnectAsync(server);
    await carol.RegisterAsync(TestGames.UniqueName("daily_carol"), TestGames.PASSWORD);

    var gameId = await StartThreePlayerGameAsync(alice, bob, carol);
    var opened = await ClockAsync(alice, gameId, playerId: 1, notBefore: time.UnixMilliseconds);
    opened.TimerMode.ShouldBe(DAILY);
    opened.DeadlineUnixMilliseconds.ShouldBe(
      time.UnixMilliseconds + (long)TurnClock.DailyTurnLength.TotalMilliseconds);

    var before = await alice.GetStateAsync(gameId);
    before.Turn.ShouldBe(1u);
    before.CurrentPlayer.ShouldBe(1u);

    // player 3 walks out of the game while player 1 still holds the turn
    await carol.SendAsync(new ResignGamePacket { GameId = gameId });
    (await carol.ExpectAsync<MembershipResultPacket>()).Result.ShouldBe(GameActionResult.Ok);
    foreach (var client in new[] { alice, bob, carol }) {
      (await client.ExpectAsync<PlayerEliminatedPacket>(packet => packet.GameId == gameId)).PlayerId.ShouldBe(3u);
      // the resignation broadcasts a fresh full state; consume it so the later requests read their own answers
      (await client.ExpectAsync<GameStatePacket>(packet => packet.GameId == gameId)).Over.ShouldBeFalse();
    }

    time.Advance(TurnClock.DailyTurnLength + TimeSpan.FromHours(1));

    // the player sitting on the overdue turn can't skip herself out of it
    await alice.SendAsync(Resolve(gameId, kick: false, turn: before.Turn, player: 1));
    (await alice.ExpectAsync<MembershipResultPacket>()).Result.ShouldBe(GameActionResult.InvalidParameters);

    // neither can a player who is out of the game
    await carol.SendAsync(Resolve(gameId, kick: false, turn: before.Turn, player: 1));
    (await carol.ExpectAsync<MembershipResultPacket>()).Result.ShouldBe(GameActionResult.PlayerEliminated);

    // nor anybody who was never in it
    using var stranger = await TestClient.ConnectAsync(server);
    await stranger.RegisterAsync(TestGames.UniqueName("daily_stranger"), TestGames.PASSWORD);
    await stranger.SendAsync(Resolve(gameId, kick: false, turn: before.Turn, player: 1));
    (await stranger.ExpectAsync<MembershipResultPacket>()).Result.ShouldBe(GameActionResult.NotInGame);

    await bob.SendAsync(Resolve(gameId + 1000, kick: false, turn: before.Turn, player: 1));
    (await bob.ExpectAsync<MembershipResultPacket>()).Result.ShouldBe(GameActionResult.GameNotFound);

    // an opponent describing a turn that isn't the one running gets nothing either
    await bob.SendAsync(Resolve(gameId, kick: false, turn: before.Turn + 7, player: 1));
    (await bob.ExpectAsync<MembershipResultPacket>()).Result.ShouldBe(GameActionResult.InvalidParameters);
    await bob.SendAsync(Resolve(gameId, kick: false, turn: before.Turn, player: 2));
    (await bob.ExpectAsync<MembershipResultPacket>()).Result.ShouldBe(GameActionResult.InvalidParameters);

    // ...and the turn really didn't move under any of those refusals
    (await alice.GetStateAsync(gameId)).CurrentPlayer.ShouldBe(1u);

    // the opponent naming the running turn does move it
    await bob.SendAsync(Resolve(gameId, kick: false, turn: before.Turn, player: 1));
    (await bob.ExpectAsync<MembershipResultPacket>(packet => packet.GameId == gameId)).Result
      .ShouldBe(GameActionResult.Ok);

    var started = await alice.ExpectAsync<TurnStartedPacket>(packet => packet.GameId == gameId);
    started.PlayerId.ShouldBe(2u);
    var handed = await bob.ExpectAsync<GameClockPacket>(packet =>
      packet.GameId == gameId && packet.PlayerId == 2 && packet.ServerUnixMilliseconds >= time.UnixMilliseconds);
    handed.DeadlineUnixMilliseconds.ShouldBe(
      time.UnixMilliseconds + (long)TurnClock.DailyTurnLength.TotalMilliseconds);

    // skipping is not eliminating: player 1 is still alive, just out of the turn she sat on
    var after = await alice.GetStateAsync(gameId);
    after.CurrentPlayer.ShouldBe(2u);
    after.Players.Single(player => player.PlayerId == 1).Alive.ShouldBeTrue();

    // replaying the request that just succeeded is stale now, and the state it described is gone
    await bob.SendAsync(Resolve(gameId, kick: false, turn: before.Turn, player: 1));
    (await bob.ExpectAsync<MembershipResultPacket>()).Result.ShouldBe(GameActionResult.InvalidParameters);
    (await alice.GetStateAsync(gameId)).CurrentPlayer.ShouldBe(2u);
  }

  [Fact]
  public async Task TestADailyTurnNeverExpiresOnItsOwnAndAKickEndsATwoPlayerGame() {
    using var database = new TempDatabase();
    var time = new ManualTimeProvider(_epoch);
    await using var server = await TestServer.StartAsync(database.Path, time);

    using var alice = await TestClient.ConnectAsync(server);
    await alice.RegisterAsync(TestGames.UniqueName("kick_alice"), TestGames.PASSWORD);
    using var bob = await TestClient.ConnectAsync(server);
    await bob.RegisterAsync(TestGames.UniqueName("kick_bob"), TestGames.PASSWORD);

    var gameId = await TestGames.StartGameAsync(alice, bob, DAILY);
    var before = await alice.GetStateAsync(gameId);

    // a day and a half with nobody playing: a daily deadline passing is not, by itself, an event
    time.Advance(TurnClock.DailyTurnLength + TimeSpan.FromHours(12));

    (await bob.TryReceiveAsync<TurnStartedPacket>(null, QUIET)).ShouldBeNull();
    (await bob.TryReceiveAsync<PlayerEliminatedPacket>(null, TimeSpan.Zero)).ShouldBeNull();
    (await bob.TryReceiveAsync<GameOverPacket>(null, TimeSpan.Zero)).ShouldBeNull();

    var idle = await bob.GetStateAsync(gameId);
    idle.Turn.ShouldBe(before.Turn);
    idle.CurrentPlayer.ShouldBe(before.CurrentPlayer);
    idle.Over.ShouldBeFalse();

    // the opponent has had enough and drops the absent player outright
    await bob.SendAsync(Resolve(gameId, kick: true, turn: before.Turn, player: 1));
    (await bob.ExpectAsync<MembershipResultPacket>(packet => packet.GameId == gameId)).Result
      .ShouldBe(GameActionResult.Ok);

    foreach (var client in new[] { alice, bob }) {
      (await client.ExpectAsync<PlayerEliminatedPacket>(packet => packet.GameId == gameId)).PlayerId.ShouldBe(1u);
      var over = await client.ExpectAsync<GameOverPacket>(packet => packet.GameId == gameId);
      over.Winner.ShouldBe(2u);
      over.Players.Single(player => player.PlayerId == 1).Alive.ShouldBeFalse();
    }

    // the game is finished, so there's nothing left to resolve and no clock to hand out
    var final = await alice.GetStateAsync(gameId);
    final.Over.ShouldBeTrue();
    final.Winner.ShouldBe(2u);

    await bob.SendAsync(Resolve(gameId, kick: true, turn: before.Turn, player: 1));
    (await bob.ExpectAsync<MembershipResultPacket>()).Result.ShouldBe(GameActionResult.GameOver);

    // the predicate skips the clock of the turn the kick ended: a finished game hands out no new one
    (await bob.TryReceiveAsync<GameClockPacket>(
      packet => packet.ServerUnixMilliseconds >= time.UnixMilliseconds, QUIET)).ShouldBeNull();
  }

  #endregion

  private static ResolveOverdueTurnPacket Resolve(ulong gameId, bool kick, uint turn, uint player) =>
    new() { GameId = gameId, Kick = kick, ExpectedTurn = turn, ExpectedPlayer = player };

  /// <summary>
  /// Waits for the clock of a turn, ignoring the ones the server sent before <paramref name="notBefore"/>
  /// </summary>
  /// <remarks>
  /// Every turn change broadcasts a clock, and the buffered ones are still there; matching on the server instant is
  /// what keeps a test from asserting on the clock of a turn that already ended
  /// </remarks>
  private static Task<GameClockPacket> ClockAsync(TestClient client, ulong gameId, uint playerId, long notBefore) =>
    client.ExpectAsync<GameClockPacket>(packet => packet.GameId == gameId && packet.PlayerId == playerId &&
      packet.ServerUnixMilliseconds >= notBefore);

  /// <summary>
  /// Starts a three player daily game, so a player can be eliminated without the game ending
  /// </summary>
  /// <returns>the id of the started game</returns>
  private static async Task<ulong> StartThreePlayerGameAsync(TestClient host, TestClient second, TestClient third) {
    await host.SendAsync(new CreateLobbyPacket {
      MaxPlayers = 3, WorldSize = TestGames.WORLD_SIZE, Tribe = 0, TimerMode = DAILY
    });
    var created = await host.ExpectAsync<CreateLobbyResponsePacket>();
    created.Result.ShouldBe(LobbyActionResult.Ok);

    foreach (var client in new[] { second, third }) {
      await client.SendAsync(new JoinLobbyPacket { LobbyId = created.LobbyId, Tribe = 0 });
      (await client.ExpectAsync<JoinLobbyResponsePacket>()).Result.ShouldBe(LobbyActionResult.Ok);
    }

    foreach (var client in new[] { host, second, third }) {
      await client.SendAsync(new SetReadyPacket { LobbyId = created.LobbyId, Ready = true });
      (await client.ExpectAsync<SetReadyResponsePacket>()).Result.ShouldBe(LobbyActionResult.Ok);
    }

    var startTimeout = TimeSpan.FromSeconds(60);
    foreach (var client in new[] { host, second, third }) {
      await client.ExpectAsync<GameStartedPacket>(packet => packet.LobbyId == created.LobbyId, startTimeout);
      await client.ExpectAsync<GameStatePacket>(packet => packet.GameId == created.LobbyId, startTimeout);
    }

    return created.LobbyId;
  }
}
