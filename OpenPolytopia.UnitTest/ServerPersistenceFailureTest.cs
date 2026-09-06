namespace OpenPolytopia;

using System;
using System.Threading.Tasks;
using Common.Network.Packets;
using Microsoft.Data.Sqlite;
using Shouldly;

public class ServerPersistenceFailureTest {
  [Fact]
  public async Task FailedSaveSendsNoSuccessAndRestoresLobbyIdentity() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);
    using var first = await TestClient.ConnectAsync(server);
    var account = await first.RegisterAsync("rollback_player", TestGames.PASSWORD);
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
      DataSource = database.Path, Pooling = false
    }.ToString());
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "CREATE TRIGGER refuse_state BEFORE INSERT ON server_state BEGIN SELECT RAISE(FAIL, 'injected failure'); END;";
    command.ExecuteNonQuery();
    await first.SendAsync(new CreateLobbyPacket { MaxPlayers = 2, WorldSize = 11 });
    await first.ShouldBeDisconnectedAsync();
    (await first.TryReceiveAsync<CreateLobbyResponsePacket>(null, TimeSpan.Zero)).ShouldBeNull();
    command.CommandText = "DROP TRIGGER refuse_state;";
    command.ExecuteNonQuery();

    using var resumed = await TestClient.ConnectAsync(server);
    (await resumed.ResumeAsync(account.Token)).Ok.ShouldBeTrue();
    await resumed.SendAsync(new GetLobbiesPacket());
    (await resumed.ExpectAsync<GetLobbiesResponsePacket>()).Lobbies.ShouldBeEmpty();
    await resumed.SendAsync(new CreateLobbyPacket { MaxPlayers = 2, WorldSize = 11 });
    var created = await resumed.ExpectAsync<CreateLobbyResponsePacket>();
    created.Result.ShouldBe(LobbyActionResult.Ok);
    created.LobbyId.ShouldBe(1ul);
  }
}
