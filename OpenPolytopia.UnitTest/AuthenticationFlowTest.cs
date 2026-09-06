namespace OpenPolytopia;

using System.Threading.Tasks;
using Common.Network.Packets;
using Server;
using Shouldly;

public class AuthenticationFlowTest {
  [Fact]
  public async Task DuplicateLoginKeepsTheExistingSession() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);
    using var client = await TestClient.ConnectAsync(server);
    var registered = await client.RegisterAsync("duplicate_login", TestGames.PASSWORD);
    var repeated = await client.LoginAsync("duplicate_login", TestGames.PASSWORD);
    repeated.Ok.ShouldBeTrue();
    repeated.PlayerId.ShouldBe(registered.PlayerId);
    repeated.Token.ShouldBe(registered.Token);
    await client.SendAsync(new GetLobbiesPacket());
    (await client.ExpectAsync<GetLobbiesResponsePacket>()).Lobbies.ShouldBeEmpty();
  }

  [Fact]
  public async Task LogoutRevokesTheTokenAndAllowsAnotherLogin() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);
    using var client = await TestClient.ConnectAsync(server);
    var registered = await client.RegisterAsync("logout_player", TestGames.PASSWORD);
    await client.SendAsync(new LogoutPacket());
    (await client.ExpectAsync<AuthenticationPacket>()).Ok.ShouldBeFalse();
    (await client.ResumeAsync(registered.Token)).Ok.ShouldBeFalse();
    (await client.LoginAsync("logout_player", TestGames.PASSWORD)).Ok.ShouldBeTrue();
  }

  [Fact]
  public async Task RateLimitedResumeIsRetryableAndDoesNotRevokeTheToken() {
    using var database = new TempDatabase();
    AuthResult registered;
    using (var store = new ServerStore(database.Path)) registered = store.Register("retry_player", TestGames.PASSWORD);
    await using var server = await TestServer.StartAsync(database.Path);
    using var client = await TestClient.ConnectAsync(server);
    for (var attempt = 0; attempt < 60; attempt++) {
      var invalid = await client.ResumeAsync("invalid");
      invalid.Ok.ShouldBeFalse();
      invalid.Retryable.ShouldBeFalse();
    }
    var limited = await client.ResumeAsync(registered.Token);
    limited.Ok.ShouldBeFalse();
    limited.Retryable.ShouldBeTrue();
    using var check = new ServerStore(database.Path);
    check.Resume(registered.Token)!.Id.ShouldBe(registered.Account.Id);
  }
}
