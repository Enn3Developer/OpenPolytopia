namespace OpenPolytopia.Server;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// How the turns of a game are timed
/// </summary>
public enum TurnTimerMode {
  /// <summary>
  /// Everyone is online: every player owns a time bank that only ticks while it's their turn, and running it dry
  /// costs a timeout
  /// </summary>
  Live,

  /// <summary>
  /// Play by mail: every turn gets its own 24 hours deadline, nothing happens automatically when it passes
  /// </summary>
  Daily
}

/// <summary>
/// What the caller should do with the turn the clock was asked about
/// </summary>
/// <remarks>
/// The clock never touches the game: it only tells the caller what its own bookkeeping says, and the caller applies
/// it (or doesn't) to the <see cref="OpenPolytopia.Common.Gameplay.Game"/>
/// </remarks>
public enum TurnClockDecision {
  /// <summary>
  /// Nothing to do: the turn is still running, or no turn is running at all
  /// </summary>
  None,

  /// <summary>
  /// The player ended their own turn, no timeout was recorded
  /// </summary>
  Completed,

  /// <summary>
  /// The turn is over on time, but the player didn't end it themselves, so a timeout was recorded against them
  /// </summary>
  TimedOut,

  /// <summary>
  /// The last timeout put the player over <see cref="TurnClock.TIMEOUTS_BEFORE_ELIMINATION"/>: the caller should
  /// eliminate them
  /// </summary>
  Eliminated,

  /// <summary>
  /// <see cref="TurnTimerMode.Daily"/> only: the deadline of the running turn has passed
  /// </summary>
  /// <remarks>
  /// Nothing happens on its own; some participant has to ask for it through <see cref="TurnClock.Skip"/> or
  /// <see cref="TurnClock.Kick"/>
  /// </remarks>
  Overdue
}

/// <summary>
/// Serializable clock state of a single player
/// </summary>
/// <remarks>
/// Plain mutable properties on purpose: this is what gets written to disk / sent over the wire, so it has to survive
/// any run of the mill serializer
/// </remarks>
public class TurnClockPlayerState {
  /// <summary>
  /// Id of the player this state belongs to
  /// </summary>
  public int PlayerId { get; set; }

  /// <summary>
  /// Seconds left in the player's bank, not counting the turn they may be playing right now
  /// </summary>
  /// <remarks>
  /// Only meaningful in <see cref="TurnTimerMode.Live"/>; while the player is on turn, the authoritative value is
  /// <see cref="TurnClockState.DeadlineUtc"/>, and this one is refreshed when the turn ends
  /// </remarks>
  public double RemainingSeconds { get; set; }

  /// <summary>
  /// How many turns this player failed to end themselves
  /// </summary>
  public int Timeouts { get; set; }

  /// <summary>
  /// Whether the clock already told the caller to eliminate this player
  /// </summary>
  public bool Eliminated { get; set; }
}

/// <summary>
/// Serializable state of a whole <see cref="TurnClock"/>
/// </summary>
/// <remarks>
/// Round tripping this through <see cref="TurnClock.FromSnapshot"/> gives back a clock that behaves exactly like the
/// one it came from: deadlines are absolute UTC instants, so a reconnect or a server restart doesn't hand anyone
/// free time
/// </remarks>
public class TurnClockState {
  /// <summary>
  /// The timing mode of the game
  /// </summary>
  public TurnTimerMode Mode { get; set; }

  /// <summary>
  /// Id of the player currently on turn; 0 when no turn is running
  /// </summary>
  public int ActivePlayer { get; set; }

  /// <summary>
  /// When the running turn started, in UTC; <c>null</c> when no turn is running
  /// </summary>
  public DateTimeOffset? TurnStartedUtc { get; set; }

  /// <summary>
  /// The absolute UTC instant the running turn expires at; <c>null</c> when no turn is running
  /// </summary>
  public DateTimeOffset? DeadlineUtc { get; set; }

  /// <summary>
  /// Clock state of every player in the game
  /// </summary>
  public List<TurnClockPlayerState> Players { get; set; } = [];
}

/// <summary>
/// Keeps track of how long every player is taking, without knowing anything about the game itself
/// </summary>
/// <remarks>
/// <para>
/// The clock is a pure bookkeeper: it's fed the current instant by the caller (nothing here reads the system clock,
/// which is what makes it testable), it never mutates a game and it never advances a turn on its own. Everything it
/// has to say comes back as a <see cref="TurnClockDecision"/> the caller is free to act on.
/// </para>
/// <para>
/// Typical server loop:
/// <code>
/// var clock = new TurnClock(TurnTimerMode.Live, game.Players.Select(player => player.Id));
/// clock.Begin(game.CurrentPlayer, DateTimeOffset.UtcNow);
/// // ...on every tick:
/// if (clock.Poll(now) == TurnClockDecision.TimedOut) {
///   var decision = clock.Skip(cities, units, now);
///   game.NextTurn();
///   if (decision == TurnClockDecision.Eliminated) {
///     game.Eliminate(playerId);
///   }
/// }
/// </code>
/// </para>
/// </remarks>
public class TurnClock {
  /// <summary>
  /// Seconds every player starts the game with in their bank
  /// </summary>
  public const double INITIAL_BANK_SECONDS = 60d;

  /// <summary>
  /// Seconds added to the bank for every turn played
  /// </summary>
  public const double TURN_BONUS_SECONDS = 8d;

  /// <summary>
  /// Seconds added to the bank for every city the player owns when the turn ends
  /// </summary>
  public const double CITY_BONUS_SECONDS = 12d;

  /// <summary>
  /// Seconds added to the bank for every unit the player owns when the turn ends
  /// </summary>
  public const double UNIT_BONUS_SECONDS = 1d;

  /// <summary>
  /// How many timeouts a player is allowed to rack up before the clock asks for their elimination
  /// </summary>
  public const int TIMEOUTS_BEFORE_ELIMINATION = 3;

  /// <summary>
  /// How long a <see cref="TurnTimerMode.Daily"/> turn lasts
  /// </summary>
  public static readonly TimeSpan DailyTurnLength = TimeSpan.FromHours(24);

  private readonly Dictionary<int, TurnClockPlayerState> _players;

  /// <summary>
  /// The timing mode of this clock
  /// </summary>
  public TurnTimerMode Mode { get; }

  /// <summary>
  /// Id of the player currently on turn; 0 when no turn is running
  /// </summary>
  public int ActivePlayer { get; private set; }

  /// <summary>
  /// When the running turn started, in UTC; <c>null</c> when no turn is running
  /// </summary>
  public DateTimeOffset? TurnStartedUtc { get; private set; }

  /// <summary>
  /// The absolute UTC instant the running turn expires at; <c>null</c> when no turn is running
  /// </summary>
  /// <remarks>
  /// This is what gets persisted, and it's never recomputed from a duration: a restart in the middle of a turn
  /// resumes against the very same instant
  /// </remarks>
  public DateTimeOffset? DeadlineUtc { get; private set; }

  /// <summary>
  /// Whether a turn is currently being timed
  /// </summary>
  public bool Running => ActivePlayer != 0;

  /// <summary>
  /// Clock state of every player, keyed by player id
  /// </summary>
  public IReadOnlyDictionary<int, TurnClockPlayerState> Players => _players;

  /// <summary>
  /// Builds a clock for a fresh game
  /// </summary>
  /// <param name="mode">how turns are timed</param>
  /// <param name="playerIds">the ids of every player in the game</param>
  public TurnClock(TurnTimerMode mode, IEnumerable<int> playerIds) {
    if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
    ArgumentNullException.ThrowIfNull(playerIds);
    Mode = mode;
    _players = playerIds.ToDictionary(
      playerId => playerId,
      playerId => new TurnClockPlayerState { PlayerId = playerId, RemainingSeconds = INITIAL_BANK_SECONDS });
  }

  private TurnClock(TurnClockState state) {
    ArgumentNullException.ThrowIfNull(state);
    if (!Enum.IsDefined(state.Mode) || state.Players == null || state.Players.Count is < 1 or > 16 ||
        state.Players.Any(p => p.PlayerId is < 1 or > 16 || !double.IsFinite(p.RemainingSeconds) ||
          p.RemainingSeconds < 0 || p.Timeouts < 0) ||
        (state.ActivePlayer == 0 && (state.DeadlineUtc != null || state.TurnStartedUtc != null)) ||
        (state.ActivePlayer != 0 && (state.DeadlineUtc == null || state.TurnStartedUtc == null ||
          state.DeadlineUtc < state.TurnStartedUtc ||
          !state.Players.Any(p => p.PlayerId == state.ActivePlayer && !p.Eliminated)))) {
      throw new ArgumentException("Invalid saved clock", nameof(state));
    }
    Mode = state.Mode;
    ActivePlayer = state.ActivePlayer;
    TurnStartedUtc = state.TurnStartedUtc?.ToUniversalTime();
    DeadlineUtc = state.DeadlineUtc?.ToUniversalTime();
    _players = state.Players.ToDictionary(
      player => player.PlayerId,
      player => new TurnClockPlayerState {
        PlayerId = player.PlayerId,
        RemainingSeconds = player.RemainingSeconds,
        Timeouts = player.Timeouts,
        Eliminated = player.Eliminated
      });
  }

  /// <summary>
  /// Rebuilds a clock from a persisted snapshot
  /// </summary>
  /// <param name="state">the snapshot, as returned by <see cref="ToSnapshot"/></param>
  /// <returns>a clock that carries on exactly where the snapshotted one left off</returns>
  public static TurnClock FromSnapshot(TurnClockState state) => new(state);

  /// <summary>
  /// Copies out the whole state of the clock, ready to be serialized
  /// </summary>
  /// <remarks>
  /// The returned object shares nothing with the clock, so mutating it afterwards is harmless
  /// </remarks>
  /// <returns>the snapshot</returns>
  public TurnClockState ToSnapshot() => new() {
    Mode = Mode,
    ActivePlayer = ActivePlayer,
    TurnStartedUtc = TurnStartedUtc,
    DeadlineUtc = DeadlineUtc,
    Players = [
      .. _players.Values.Select(player => new TurnClockPlayerState {
        PlayerId = player.PlayerId,
        RemainingSeconds = player.RemainingSeconds,
        Timeouts = player.Timeouts,
        Eliminated = player.Eliminated
      })
    ]
  };

  /// <summary>
  /// Starts timing a turn
  /// </summary>
  /// <remarks>
  /// In <see cref="TurnTimerMode.Live"/> the deadline is whatever is left in the player's bank; in
  /// <see cref="TurnTimerMode.Daily"/> it's <see cref="DailyTurnLength"/> from now. Only the player passed here has
  /// a clock running: everyone else's bank is frozen
  /// </remarks>
  /// <param name="playerId">the player whose turn it is</param>
  /// <param name="now">the current instant</param>
  /// <exception cref="InvalidOperationException">if a turn is already running</exception>
  /// <exception cref="ArgumentException">if the player isn't part of this clock, or was already eliminated</exception>
  public void Begin(int playerId, DateTimeOffset now) {
    if (Running) {
      throw new InvalidOperationException($"turn of player {ActivePlayer} is still running");
    }

    var player = PlayerOrThrow(playerId);
    if (player.Eliminated) {
      throw new ArgumentException($"player {playerId} was eliminated", nameof(playerId));
    }

    var start = now.ToUniversalTime();
    ActivePlayer = playerId;
    TurnStartedUtc = start;
    DeadlineUtc = start + (Mode == TurnTimerMode.Daily
      ? DailyTurnLength
      : TimeSpan.FromSeconds(Math.Max(0d, player.RemainingSeconds)));
  }

  /// <summary>
  /// Whether the running turn is past its deadline
  /// </summary>
  /// <param name="now">the current instant</param>
  /// <returns>true if a turn is running and its deadline has passed</returns>
  public bool IsExpired(DateTimeOffset now) => DeadlineUtc is { } deadline && now.ToUniversalTime() >= deadline;

  /// <summary>
  /// Asks the clock what it thinks of the running turn, without changing anything
  /// </summary>
  /// <param name="now">the current instant</param>
  /// <returns>
  /// <see cref="TurnClockDecision.TimedOut"/> if a <see cref="TurnTimerMode.Live"/> turn ran out of bank,
  /// <see cref="TurnClockDecision.Overdue"/> if a <see cref="TurnTimerMode.Daily"/> turn is past its deadline,
  /// <see cref="TurnClockDecision.None"/> otherwise
  /// </returns>
  public TurnClockDecision Poll(DateTimeOffset now) {
    if (!Running || !IsExpired(now)) {
      return TurnClockDecision.None;
    }

    return Mode == TurnTimerMode.Daily ? TurnClockDecision.Overdue : TurnClockDecision.TimedOut;
  }

  /// <summary>
  /// How much time the running turn has left
  /// </summary>
  /// <param name="now">the current instant</param>
  /// <returns><see cref="TimeSpan.Zero"/> if no turn is running or the deadline already passed</returns>
  public TimeSpan Remaining(DateTimeOffset now) {
    if (DeadlineUtc is not { } deadline) {
      return TimeSpan.Zero;
    }

    var left = deadline - now.ToUniversalTime();
    return left > TimeSpan.Zero ? left : TimeSpan.Zero;
  }

  /// <summary>
  /// How much time a player has banked
  /// </summary>
  /// <remarks>
  /// For the player on turn this is the live value, i.e. it shrinks as the turn goes on; for everybody else it's
  /// frozen. Always <see cref="TimeSpan.Zero"/> outside of <see cref="TurnTimerMode.Live"/>, where banks are unused
  /// </remarks>
  /// <param name="playerId">the player to look at</param>
  /// <param name="now">the current instant</param>
  /// <returns>the seconds left in the player's bank, never negative</returns>
  /// <exception cref="ArgumentException">if the player isn't part of this clock</exception>
  public TimeSpan BankOf(int playerId, DateTimeOffset now) {
    var player = PlayerOrThrow(playerId);
    if (Mode == TurnTimerMode.Daily) {
      return TimeSpan.Zero;
    }

    return playerId == ActivePlayer ? Remaining(now) : TimeSpan.FromSeconds(Math.Max(0d, player.RemainingSeconds));
  }

  /// <summary>
  /// Ends the running turn because the player asked to
  /// </summary>
  /// <remarks>
  /// This never counts as a timeout, not even if the deadline slipped by while the packet was in flight: the player
  /// did play their turn
  /// </remarks>
  /// <param name="cities">how many cities the player owns now</param>
  /// <param name="units">how many units the player owns now</param>
  /// <param name="now">the current instant</param>
  /// <returns>always <see cref="TurnClockDecision.Completed"/></returns>
  /// <exception cref="InvalidOperationException">if no turn is running</exception>
  public TurnClockDecision Complete(int cities, int units, DateTimeOffset now) {
    EndTurn(cities, units, now);
    return TurnClockDecision.Completed;
  }

  /// <summary>
  /// Ends the running turn against the will of the player, recording a timeout
  /// </summary>
  /// <remarks>
  /// In <see cref="TurnTimerMode.Live"/> this is what the server calls once <see cref="Poll"/> reports
  /// <see cref="TurnClockDecision.TimedOut"/>; in <see cref="TurnTimerMode.Daily"/> it's what a participant asking
  /// to skip an <see cref="TurnClockDecision.Overdue"/> turn triggers. The turn still earns its bonus, otherwise a
  /// single timeout would leave the player with an empty bank forever
  /// </remarks>
  /// <param name="cities">how many cities the player owns now</param>
  /// <param name="units">how many units the player owns now</param>
  /// <param name="now">the current instant</param>
  /// <returns>
  /// <see cref="TurnClockDecision.Eliminated"/> if this was the player's
  /// <see cref="TIMEOUTS_BEFORE_ELIMINATION"/>th timeout, <see cref="TurnClockDecision.TimedOut"/> otherwise
  /// </returns>
  /// <exception cref="InvalidOperationException">if no turn is running, or the turn isn't expired yet</exception>
  public TurnClockDecision Skip(int cities, int units, DateTimeOffset now) {
    if (!Running) {
      throw new InvalidOperationException("no turn is running");
    }

    if (!IsExpired(now)) {
      throw new InvalidOperationException($"turn of player {ActivePlayer} hasn't expired yet");
    }

    var player = _players[ActivePlayer];
    EndTurn(cities, units, now);

    player.Timeouts++;
    if (Mode == TurnTimerMode.Daily || player.Timeouts < TIMEOUTS_BEFORE_ELIMINATION) {
      return TurnClockDecision.TimedOut;
    }

    player.Eliminated = true;
    return TurnClockDecision.Eliminated;
  }

  /// <summary>
  /// Drops the player of an expired turn outright, without waiting for their remaining timeouts
  /// </summary>
  /// <remarks>
  /// Meant for the kick a participant can ask for on an <see cref="TurnClockDecision.Overdue"/>
  /// <see cref="TurnTimerMode.Daily"/> turn; as everything else here it only marks the clock's own state, the caller
  /// is the one who has to remove the player from the game
  /// </remarks>
  /// <param name="now">the current instant</param>
  /// <returns>always <see cref="TurnClockDecision.Eliminated"/></returns>
  /// <exception cref="InvalidOperationException">if no turn is running, or the turn isn't expired yet</exception>
  public TurnClockDecision Kick(DateTimeOffset now) {
    if (!Running) {
      throw new InvalidOperationException("no turn is running");
    }

    if (!IsExpired(now)) {
      throw new InvalidOperationException($"turn of player {ActivePlayer} hasn't expired yet");
    }

    var player = _players[ActivePlayer];
    EndTurn(0, 0, now);

    player.Timeouts = TIMEOUTS_BEFORE_ELIMINATION;
    player.Eliminated = true;
    return TurnClockDecision.Eliminated;
  }

  /// <summary>
  /// Banks the earned time and stops the clock
  /// </summary>
  /// <param name="cities">how many cities the player owns now</param>
  /// <param name="units">how many units the player owns now</param>
  /// <param name="now">the current instant</param>
  /// <exception cref="InvalidOperationException">if no turn is running</exception>
  private void EndTurn(int cities, int units, DateTimeOffset now) {
    if (!Running) {
      throw new InvalidOperationException("no turn is running");
    }

    var player = _players[ActivePlayer];
    if (Mode == TurnTimerMode.Live) {
      var bonus = TURN_BONUS_SECONDS + (CITY_BONUS_SECONDS * cities) + (UNIT_BONUS_SECONDS * units);
      player.RemainingSeconds = Remaining(now).TotalSeconds + bonus;
    }

    ActivePlayer = 0;
    TurnStartedUtc = null;
    DeadlineUtc = null;
  }

  /// <summary>
  /// Looks up a player's state
  /// </summary>
  /// <param name="playerId">the player to look for</param>
  /// <returns>the player's clock state</returns>
  /// <exception cref="ArgumentException">if the player isn't part of this clock</exception>
  private TurnClockPlayerState PlayerOrThrow(int playerId) =>
    _players.TryGetValue(playerId, out var player)
      ? player
      : throw new ArgumentException($"player {playerId} isn't part of this clock", nameof(playerId));
}
