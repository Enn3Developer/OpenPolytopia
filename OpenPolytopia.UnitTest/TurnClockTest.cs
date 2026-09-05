namespace OpenPolytopia;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Server;
using Shouldly;

public class TurnClockTest {
  // fixed instant so nothing here depends on the wall clock
  private static readonly DateTimeOffset _start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

  private static readonly int[] _playerIds = [1, 2];

  private static TurnClock NewClock(TurnTimerMode mode = TurnTimerMode.Live) => new(mode, _playerIds);

  private static TurnClockState RoundTrip(TurnClockState state) =>
    JsonSerializer.Deserialize<TurnClockState>(JsonSerializer.Serialize(state)).ShouldNotBeNull();

  [Fact]
  public void TestEveryPlayerStartsWithTheInitialBank() {
    var clock = NewClock();

    clock.Running.ShouldBeFalse();
    foreach (var playerId in _playerIds) {
      clock.BankOf(playerId, _start).ShouldBe(TimeSpan.FromSeconds(TurnClock.INITIAL_BANK_SECONDS));
      clock.Players[playerId].Timeouts.ShouldBe(0);
      clock.Players[playerId].Eliminated.ShouldBeFalse();
    }
  }

  [Fact]
  public void TestBeginUsesTheBankAsDeadline() {
    var clock = NewClock();

    clock.Begin(1, _start);

    clock.Running.ShouldBeTrue();
    clock.ActivePlayer.ShouldBe(1);
    clock.TurnStartedUtc.ShouldBe(_start);
    clock.DeadlineUtc.ShouldBe(_start.AddSeconds(TurnClock.INITIAL_BANK_SECONDS));
  }

  [Fact]
  public void TestBeginStoresTheDeadlineInUtc() {
    var clock = NewClock();

    // same instant, expressed in a different offset: the clock has to normalize it
    clock.Begin(1, _start.ToOffset(TimeSpan.FromHours(5)));

    clock.DeadlineUtc.ShouldNotBeNull().Offset.ShouldBe(TimeSpan.Zero);
    clock.DeadlineUtc.ShouldBe(_start.AddSeconds(TurnClock.INITIAL_BANK_SECONDS));
  }

  [Fact]
  public void TestOnlyTheActivePlayerClockRuns() {
    var clock = NewClock();
    clock.Begin(1, _start);

    var later = _start.AddSeconds(20);

    clock.BankOf(1, later).ShouldBe(TimeSpan.FromSeconds(40));
    clock.BankOf(2, later).ShouldBe(TimeSpan.FromSeconds(TurnClock.INITIAL_BANK_SECONDS));
  }

  [Fact]
  public void TestBeginRejectsASecondTurn() {
    var clock = NewClock();
    clock.Begin(1, _start);

    Should.Throw<InvalidOperationException>(() => clock.Begin(2, _start));
  }

  [Fact]
  public void TestBeginRejectsAnUnknownPlayer() {
    var clock = NewClock();

    Should.Throw<ArgumentException>(() => clock.Begin(42, _start));
  }

  [Fact]
  public void TestCompleteBanksTheTurnBonus() {
    var clock = NewClock();
    clock.Begin(1, _start);

    // 20 seconds spent, 2 cities and 3 units owned
    clock.Complete(2, 3, _start.AddSeconds(20)).ShouldBe(TurnClockDecision.Completed);

    var expected = 40d + TurnClock.TURN_BONUS_SECONDS + (2 * TurnClock.CITY_BONUS_SECONDS) +
                   (3 * TurnClock.UNIT_BONUS_SECONDS);
    clock.BankOf(1, _start.AddSeconds(20)).ShouldBe(TimeSpan.FromSeconds(expected));
    clock.Running.ShouldBeFalse();
    clock.DeadlineUtc.ShouldBeNull();
  }

  [Fact]
  public void TestCompleteNeverCountsAsATimeout() {
    var clock = NewClock();
    clock.Begin(1, _start);

    // completing late still isn't a timeout: the player did play
    clock.Complete(0, 0, _start.AddSeconds(90)).ShouldBe(TurnClockDecision.Completed);

    clock.Players[1].Timeouts.ShouldBe(0);
    clock.Players[1].Eliminated.ShouldBeFalse();
  }

  [Fact]
  public void TestCompleteWithoutARunningTurnThrows() {
    var clock = NewClock();

    Should.Throw<InvalidOperationException>(() => clock.Complete(0, 0, _start));
  }

  [Fact]
  public void TestBankNeverGoesNegative() {
    var clock = NewClock();
    clock.Begin(1, _start);

    clock.Remaining(_start.AddSeconds(500)).ShouldBe(TimeSpan.Zero);
    clock.Complete(0, 0, _start.AddSeconds(500));

    clock.BankOf(1, _start).ShouldBe(TimeSpan.FromSeconds(TurnClock.TURN_BONUS_SECONDS));
  }

  [Fact]
  public void TestIsExpiredOnlyOnceTheBankIsDry() {
    var clock = NewClock();
    clock.Begin(1, _start);

    clock.IsExpired(_start.AddSeconds(59)).ShouldBeFalse();
    clock.IsExpired(_start.AddSeconds(60)).ShouldBeTrue();
  }

  [Fact]
  public void TestIsExpiredIsFalseWithoutARunningTurn() {
    var clock = NewClock();

    clock.IsExpired(_start.AddYears(1)).ShouldBeFalse();
    clock.Poll(_start.AddYears(1)).ShouldBe(TurnClockDecision.None);
  }

  [Fact]
  public void TestPollReportsALiveTimeout() {
    var clock = NewClock();
    clock.Begin(1, _start);

    clock.Poll(_start.AddSeconds(30)).ShouldBe(TurnClockDecision.None);
    clock.Poll(_start.AddSeconds(60)).ShouldBe(TurnClockDecision.TimedOut);

    // polling doesn't change anything: the turn is still the caller's to end
    clock.Running.ShouldBeTrue();
    clock.Players[1].Timeouts.ShouldBe(0);
  }

  [Fact]
  public void TestSkipRecordsATimeoutAndEndsTheTurn() {
    var clock = NewClock();
    clock.Begin(1, _start);

    clock.Skip(1, 0, _start.AddSeconds(60)).ShouldBe(TurnClockDecision.TimedOut);

    clock.Players[1].Timeouts.ShouldBe(1);
    clock.Players[1].Eliminated.ShouldBeFalse();
    clock.Running.ShouldBeFalse();
    // a timed out turn still earns its bonus, otherwise the bank could never recover
    clock.BankOf(1, _start).ShouldBe(TimeSpan.FromSeconds(TurnClock.TURN_BONUS_SECONDS + TurnClock.CITY_BONUS_SECONDS));
  }

  [Fact]
  public void TestSkipBeforeTheDeadlineThrows() {
    var clock = NewClock();
    clock.Begin(1, _start);

    Should.Throw<InvalidOperationException>(() => clock.Skip(0, 0, _start.AddSeconds(30)));
    clock.Players[1].Timeouts.ShouldBe(0);
  }

  [Fact]
  public void TestThreeTimeoutsEliminateThePlayer() {
    var clock = NewClock();

    for (var turn = 0; turn < 2; turn++) {
      clock.Begin(1, _start);
      clock.Skip(0, 0, _start.AddSeconds(1000)).ShouldBe(TurnClockDecision.TimedOut);
    }

    clock.Begin(1, _start);
    clock.Skip(0, 0, _start.AddSeconds(1000)).ShouldBe(TurnClockDecision.Eliminated);

    clock.Players[1].Timeouts.ShouldBe(TurnClock.TIMEOUTS_BEFORE_ELIMINATION);
    clock.Players[1].Eliminated.ShouldBeTrue();
    // the other player is untouched
    clock.Players[2].Timeouts.ShouldBe(0);
  }

  [Fact]
  public void TestVoluntaryCompletionsDoNotPushTowardsElimination() {
    var clock = NewClock();

    clock.Begin(1, _start);
    clock.Skip(0, 0, _start.AddSeconds(1000));
    for (var turn = 0; turn < 5; turn++) {
      clock.Begin(1, _start);
      clock.Complete(0, 0, _start.AddSeconds(1));
    }

    clock.Begin(1, _start);
    clock.Skip(0, 0, _start.AddSeconds(1000)).ShouldBe(TurnClockDecision.TimedOut);
    clock.Players[1].Timeouts.ShouldBe(2);
    clock.Players[1].Eliminated.ShouldBeFalse();
  }

  [Fact]
  public void TestBeginRejectsAnEliminatedPlayer() {
    var clock = NewClock();
    for (var turn = 0; turn < TurnClock.TIMEOUTS_BEFORE_ELIMINATION; turn++) {
      clock.Begin(1, _start);
      clock.Skip(0, 0, _start.AddSeconds(1000));
    }

    Should.Throw<ArgumentException>(() => clock.Begin(1, _start));
  }

  [Fact]
  public void TestDailyDeadlineIsTwentyFourHours() {
    var clock = NewClock(TurnTimerMode.Daily);
    clock.Begin(1, _start);

    clock.DeadlineUtc.ShouldBe(_start.AddHours(24));
    clock.Remaining(_start.AddHours(6)).ShouldBe(TimeSpan.FromHours(18));
  }

  [Fact]
  public void TestDailyTurnsIgnoreTheBank() {
    var clock = NewClock(TurnTimerMode.Daily);
    clock.Begin(1, _start);
    clock.Complete(3, 4, _start.AddHours(6));

    clock.BankOf(1, _start).ShouldBe(TimeSpan.Zero);

    // the next turn gets its own full 24 hours, no matter how long the previous one took
    clock.Begin(1, _start.AddHours(6));
    clock.DeadlineUtc.ShouldBe(_start.AddHours(30));
  }

  [Fact]
  public void TestDailyOverdueDoesNotAdvanceOnItsOwn() {
    var clock = NewClock(TurnTimerMode.Daily);
    clock.Begin(1, _start);

    var late = _start.AddHours(30);
    clock.IsExpired(late).ShouldBeTrue();
    clock.Poll(late).ShouldBe(TurnClockDecision.Overdue);

    // still the same player's turn until somebody asks for a skip
    clock.Running.ShouldBeTrue();
    clock.ActivePlayer.ShouldBe(1);
    clock.Players[1].Timeouts.ShouldBe(0);
  }

  [Fact]
  public void TestDailySkipEndsTheOverdueTurn() {
    var clock = NewClock(TurnTimerMode.Daily);
    clock.Begin(1, _start);

    clock.Skip(0, 0, _start.AddHours(30)).ShouldBe(TurnClockDecision.TimedOut);

    clock.Running.ShouldBeFalse();
    clock.Players[1].Timeouts.ShouldBe(1);
  }

  [Fact]
  public void TestKickEliminatesImmediately() {
    var clock = NewClock(TurnTimerMode.Daily);
    clock.Begin(1, _start);

    clock.Kick(_start.AddHours(30)).ShouldBe(TurnClockDecision.Eliminated);

    clock.Players[1].Eliminated.ShouldBeTrue();
    clock.Players[1].Timeouts.ShouldBe(TurnClock.TIMEOUTS_BEFORE_ELIMINATION);
    clock.Running.ShouldBeFalse();
  }

  [Fact]
  public void TestKickBeforeTheDeadlineThrows() {
    var clock = NewClock(TurnTimerMode.Daily);
    clock.Begin(1, _start);

    Should.Throw<InvalidOperationException>(() => clock.Kick(_start.AddHours(23)));
    clock.Players[1].Eliminated.ShouldBeFalse();
  }

  [Fact]
  public void TestSnapshotRoundTripKeepsTheDeadline() {
    var clock = NewClock();
    clock.Begin(1, _start);
    clock.Complete(1, 2, _start.AddSeconds(10));
    clock.Begin(2, _start.AddSeconds(10));

    var restored = TurnClock.FromSnapshot(RoundTrip(clock.ToSnapshot()));

    restored.Mode.ShouldBe(TurnTimerMode.Live);
    restored.ActivePlayer.ShouldBe(2);
    restored.DeadlineUtc.ShouldBe(clock.DeadlineUtc);
    restored.TurnStartedUtc.ShouldBe(clock.TurnStartedUtc);
    restored.BankOf(1, _start).ShouldBe(clock.BankOf(1, _start));
  }

  [Fact]
  public void TestRestartDoesNotGiveBackTheTimeSpentOffline() {
    var clock = NewClock();
    clock.Begin(1, _start);

    // server goes down for 45 seconds and comes back from the snapshot
    var restored = TurnClock.FromSnapshot(RoundTrip(clock.ToSnapshot()));
    var back = _start.AddSeconds(45);

    restored.Remaining(back).ShouldBe(TimeSpan.FromSeconds(15));
    restored.IsExpired(back).ShouldBeFalse();
    restored.Poll(_start.AddSeconds(61)).ShouldBe(TurnClockDecision.TimedOut);
  }

  [Fact]
  public void TestSnapshotKeepsTimeoutCounters() {
    var clock = NewClock();
    clock.Begin(1, _start);
    clock.Skip(0, 0, _start.AddSeconds(1000));
    clock.Begin(1, _start);
    clock.Skip(0, 0, _start.AddSeconds(1000));

    var restored = TurnClock.FromSnapshot(RoundTrip(clock.ToSnapshot()));

    restored.Players[1].Timeouts.ShouldBe(2);
    // the third timeout still eliminates, counters survived the restart
    restored.Begin(1, _start);
    restored.Skip(0, 0, _start.AddSeconds(1000)).ShouldBe(TurnClockDecision.Eliminated);
  }

  [Fact]
  public void TestSnapshotIsADetachedCopy() {
    var clock = NewClock();
    var snapshot = clock.ToSnapshot();

    snapshot.Players[0].Timeouts = 7;
    snapshot.ActivePlayer = 99;

    clock.Players[_playerIds[0]].Timeouts.ShouldBe(0);
    clock.ActivePlayer.ShouldBe(0);
  }

  [Fact]
  public void TestSnapshotOfADailyClockKeepsItsMode() {
    var clock = NewClock(TurnTimerMode.Daily);
    clock.Begin(1, _start);

    var restored = TurnClock.FromSnapshot(RoundTrip(clock.ToSnapshot()));

    restored.Mode.ShouldBe(TurnTimerMode.Daily);
    restored.Poll(_start.AddHours(25)).ShouldBe(TurnClockDecision.Overdue);
  }

  [Fact]
  public void TestBankOfAnUnknownPlayerThrows() {
    var clock = NewClock();

    Should.Throw<ArgumentException>(() => clock.BankOf(42, _start));
  }

  [Fact]
  public void TestFullLiveRoundOfTurns() {
    var clock = NewClock();
    var now = _start;

    clock.Begin(1, now);
    now = now.AddSeconds(15);
    clock.Complete(1, 0, now).ShouldBe(TurnClockDecision.Completed);

    clock.Begin(2, now);
    now = now.AddSeconds(25);
    clock.Complete(1, 1, now).ShouldBe(TurnClockDecision.Completed);

    clock.BankOf(1, now).ShouldBe(TimeSpan.FromSeconds(45 + 8 + 12));
    clock.BankOf(2, now).ShouldBe(TimeSpan.FromSeconds(35 + 8 + 12 + 1));

    clock.Begin(1, now);
    clock.DeadlineUtc.ShouldBe(now.AddSeconds(65));
  }
  [Fact]
  public void TestDailySkipsNeverAutomaticallyEliminate() {
    var clock = NewClock(TurnTimerMode.Daily);
    for (var turn = 0; turn < 4; turn++) {
      var now = _start.AddDays(turn);
      clock.Begin(1, now);
      clock.Skip(1, 1, now.AddDays(1)).ShouldBe(TurnClockDecision.TimedOut);
    }
    clock.Players[1].Eliminated.ShouldBeFalse();
  }

  [Fact]
  public void TestRestoreRejectsAnActiveClockWithoutDeadline() {
    var snapshot = NewClock().ToSnapshot();
    snapshot.ActivePlayer = 1;
    Should.Throw<ArgumentException>(() => TurnClock.FromSnapshot(snapshot));
  }
}
