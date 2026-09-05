namespace OpenPolytopia;

using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Common.Gameplay;
using Common.Network.Packets;
using Server;
using Shouldly;

public class TimerMigrationTest {
  [Fact]
  public async Task PreTimerGameGetsOnePersistedDailyDeadlineOnUpgrade() {
    using var database = new TempDatabase();
    var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
    ulong id;
    string token;
    await using (var server = await TestServer.StartAsync(database.Path, time)) {
      using var alice = await TestClient.ConnectAsync(server);
      token = (await alice.RegisterAsync(TestGames.UniqueName("legacy_alice"), TestGames.PASSWORD)).Token;
      using var bob = await TestClient.ConnectAsync(server);
      await bob.RegisterAsync(TestGames.UniqueName("legacy_bob"), TestGames.PASSWORD);
      id = await TestGames.StartGameAsync(alice, bob);
    }
    using (var store = new ServerStore(database.Path)) {
      var state = JsonNode.Parse(store.LoadState()!)!;
      state["Version"] = 1;
      foreach (var game in state["Games"]!.AsArray()) game!.AsObject().Remove("Clock");
      store.SaveState(state.ToJsonString());
    }
    var deadline = time.GetUtcNow().Add(TurnClock.DailyTurnLength).ToUnixTimeMilliseconds();
    for (var restart = 0; restart < 2; restart++) {
      await using var server = await TestServer.StartAsync(database.Path, time);
      using var alice = await TestClient.ConnectAsync(server);
      (await alice.ResumeAsync(token)).Ok.ShouldBeTrue();
      await alice.SendAsync(new JoinGamePacket { GameId = id });
      var clock = await alice.ExpectAsync<GameClockPacket>(packet => packet.GameId == id);
      clock.TimerMode.ShouldBe((uint)TurnTimerMode.Daily);
      clock.DeadlineUnixMilliseconds.ShouldBe(deadline);
      using var store = new ServerStore(database.Path);
      JsonNode.Parse(store.LoadState()!)!["Version"]!.GetValue<int>().ShouldBe(2);
      time.Advance(TimeSpan.FromHours(1));
    }
  }
}
