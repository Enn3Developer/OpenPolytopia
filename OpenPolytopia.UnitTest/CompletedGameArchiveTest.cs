namespace OpenPolytopia;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Common.Gameplay;
using Common.Network.Packets;
using Microsoft.Data.Sqlite;
using Server;
using Shouldly;

public class CompletedGameArchiveTest {
  [Fact]
  public async Task CompletedGameLeavesLiveSnapshotAndRemainsAccessibleAfterRestart() {
    using var database = new TempDatabase();
    ulong id;
    string token;
    uint accountId;
    await using (var server = await TestServer.StartAsync(database.Path)) {
      using var alice = await TestClient.ConnectAsync(server);
      var auth = await alice.RegisterAsync(TestGames.UniqueName("archive_alice"), TestGames.PASSWORD);
      token = auth.Token;
      accountId = auth.PlayerId;
      using var bob = await TestClient.ConnectAsync(server);
      await bob.RegisterAsync(TestGames.UniqueName("archive_bob"), TestGames.PASSWORD);
      id = await TestGames.StartGameAsync(alice, bob);
      await alice.SendAsync(new ResignGamePacket { GameId = id });
      (await alice.ExpectAsync<MembershipResultPacket>()).Result.ShouldBe(GameActionResult.Ok);
      using var store = new ServerStore(database.Path);
      using var snapshot = JsonDocument.Parse(store.LoadState()!);
      snapshot.RootElement.GetProperty("Games").GetArrayLength().ShouldBe(0);
      store.CompletedGameIds(accountId).ShouldContain(id);
      store.LoadCompletedGame(id, uint.MaxValue).ShouldBeNull();
      await alice.SendAsync(new JoinGamePacket { GameId = id });
      (await alice.ExpectAsync<GameStatePacket>(p => p.GameId == id && p.Over)).Result.ShouldBe(GameActionResult.Ok);
    }
    await using var restarted = await TestServer.StartAsync(database.Path);
    using var member = await TestClient.ConnectAsync(restarted);
    (await member.ResumeAsync(token)).Ok.ShouldBeTrue();
    await member.SendAsync(new GetMyGamesPacket());
    (await member.ExpectAsync<MyGamesPacket>()).GameIds.ShouldContain(id);
    await member.SendAsync(new JoinGamePacket { GameId = id });
    (await member.ExpectAsync<GameStatePacket>(p => p.GameId == id)).Over.ShouldBeTrue();
  }

  [Fact]
  public void ArchiveFailureRollsBackLiveSnapshotAndAccountRename() {
    using var database = new TempDatabase();
    using var store = new ServerStore(database.Path);
    var auth = store.Register("archive_owner", TestGames.PASSWORD);
    store.SaveState("before");
    using var connection = new SqliteConnection($"Data Source={database.Path};Pooling=False");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "CREATE TRIGGER refuse_archive BEFORE INSERT ON completed_games BEGIN SELECT RAISE(FAIL, 'injected failure'); END;";
    command.ExecuteNonQuery();
    Should.Throw<SqliteException>(() => store.SaveState("after", new System.Collections.Generic.Dictionary<uint, string> {
      [auth.Account.Id] = "renamed"
    }, [new ArchivedGame(ulong.MaxValue, "result", [auth.Account.Id])]));
    store.LoadState().ShouldBe("before");
    store.Resume(auth.Token)!.DisplayName.ShouldBe("archive_owner");
    store.CompletedGameIds(auth.Account.Id).ShouldBeEmpty();
    command.CommandText = "DROP TRIGGER refuse_archive";
    command.ExecuteNonQuery();
    store.SaveState("after", completedGames: [new ArchivedGame(ulong.MaxValue, "result", [auth.Account.Id])]);
    store.LoadCompletedGame(ulong.MaxValue, auth.Account.Id).ShouldBe("result");
  }

  [Fact]
  public void VersionOneDatabaseMigratesWithoutLosingState() {
    using var database = new TempDatabase();
    using (var store = new ServerStore(database.Path)) store.SaveState("legacy");
    using (var connection = new SqliteConnection($"Data Source={database.Path};Pooling=False")) {
      connection.Open();
      using var command = connection.CreateCommand();
      command.CommandText = "DROP TABLE completed_game_members; DROP TABLE completed_games; PRAGMA user_version = 1;";
      command.ExecuteNonQuery();
    }
    using var migrated = new ServerStore(database.Path);
    migrated.LoadState().ShouldBe("legacy");
    migrated.CompletedGameIds(1).ShouldBeEmpty();
  }
}
