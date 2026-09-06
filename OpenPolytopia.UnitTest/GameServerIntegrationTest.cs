namespace OpenPolytopia;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.Gameplay;
using Common.Network.Packets;
using Godot;
using Shouldly;

/// <summary>
/// End to end tests of <see cref="Server.GameServer"/> over a real loopback TCP socket and a real SQLite database
/// </summary>
/// <remarks>
/// Nothing here reaches into the server's internals: every assertion is made on the packets a client actually
/// receives, so these tests fail exactly when a real client would break
/// </remarks>
public class GameServerIntegrationTest {
  #region Accounts

  [Fact]
  public async Task TestRegisterCreatesAnAccountAndASession() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);
    using var client = await TestClient.ConnectAsync(server);

    var username = TestGames.UniqueName("alice");
    var response = await client.RegisterAsync(username, TestGames.PASSWORD);

    response.Ok.ShouldBeTrue();
    response.PlayerId.ShouldNotBe(0u);
    response.Name.ShouldBe(username);
    response.Token.ShouldNotBeNullOrEmpty();
  }

  [Fact]
  public async Task TestRegisteringTheSameUsernameTwiceFails() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);
    var username = TestGames.UniqueName("bob");

    using var first = await TestClient.ConnectAsync(server);
    (await first.RegisterAsync(username, TestGames.PASSWORD)).Ok.ShouldBeTrue();

    // a different transport, so the failure can only come from the duplicate username
    using var second = await TestClient.ConnectAsync(server);
    (await second.RegisterAsync(username, TestGames.PASSWORD)).Ok.ShouldBeFalse();
    (await second.RegisterAsync(username.ToUpperInvariant(), TestGames.PASSWORD)).Ok.ShouldBeFalse();
  }

  [Fact]
  public async Task TestLoginRejectsWrongCredentialsAndAcceptsTheRightOnes() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);
    var username = TestGames.UniqueName("carol");

    using var registrar = await TestClient.ConnectAsync(server);
    var registered = await registrar.RegisterAsync(username, TestGames.PASSWORD);

    using var client = await TestClient.ConnectAsync(server);
    (await client.LoginAsync(username, "wrong password!")).Ok.ShouldBeFalse();
    (await client.LoginAsync(TestGames.UniqueName("nobody"), TestGames.PASSWORD)).Ok.ShouldBeFalse();

    var ok = await client.LoginAsync(username, TestGames.PASSWORD);
    ok.Ok.ShouldBeTrue();
    ok.PlayerId.ShouldBe(registered.PlayerId);
  }

  [Fact]
  public async Task TestSessionResumesAfterADisconnect() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);
    var username = TestGames.UniqueName("dave");

    string token;
    uint accountId;
    using (var client = await TestClient.ConnectAsync(server)) {
      var registered = await client.RegisterAsync(username, TestGames.PASSWORD);
      token = registered.Token;
      accountId = registered.PlayerId;
    }

    using var reconnected = await TestClient.ConnectAsync(server);
    var resumed = await reconnected.ResumeAsync(token);

    resumed.Ok.ShouldBeTrue();
    resumed.PlayerId.ShouldBe(accountId);
    resumed.Name.ShouldBe(username);
  }

  [Fact]
  public async Task TestSessionResumesOnANewServerSharingTheDatabase() {
    using var database = new TempDatabase();
    var username = TestGames.UniqueName("erin");

    string token;
    uint accountId;
    await using (var first = await TestServer.StartAsync(database.Path)) {
      using var client = await TestClient.ConnectAsync(first);
      var registered = await client.RegisterAsync(username, TestGames.PASSWORD);
      token = registered.Token;
      accountId = registered.PlayerId;
    }

    await using var second = await TestServer.StartAsync(database.Path);

    using var resuming = await TestClient.ConnectAsync(second);
    var resumed = await resuming.ResumeAsync(token);
    resumed.Ok.ShouldBeTrue();
    resumed.PlayerId.ShouldBe(accountId);

    // the password still works too, i.e. the whole account survived and not just the session
    using var logging = await TestClient.ConnectAsync(second);
    (await logging.LoginAsync(username, TestGames.PASSWORD)).PlayerId.ShouldBe(accountId);
  }

  [Fact]
  public async Task TestUnauthenticatedClientsGetKicked() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);
    using var client = await TestClient.ConnectAsync(server);

    // handshake done, but no account: anything but an authentication packet is a kick
    await client.SendAsync(new GetLobbiesPacket());

    await client.ShouldBeDisconnectedAsync();
  }

  #endregion

  #region Lobbies

  [Fact]
  public async Task TestLobbyMembershipSurvivesADisconnectAndARestart() {
    using var database = new TempDatabase();
    var username = TestGames.UniqueName("frank");

    ulong lobbyId;
    string token;
    uint accountId;
    await using (var first = await TestServer.StartAsync(database.Path)) {
      using var client = await TestClient.ConnectAsync(first);
      var registered = await client.RegisterAsync(username, TestGames.PASSWORD);
      token = registered.Token;
      accountId = registered.PlayerId;

      await client.SendAsync(new CreateLobbyPacket { MaxPlayers = 2, WorldSize = TestGames.WORLD_SIZE, Tribe = 0 });
      var created = await client.ExpectAsync<CreateLobbyResponsePacket>();
      created.Result.ShouldBe(LobbyActionResult.Ok);
      lobbyId = created.LobbyId;
    }

    await using var second = await TestServer.StartAsync(database.Path);
    using var reconnected = await TestClient.ConnectAsync(second);
    (await reconnected.ResumeAsync(token)).Ok.ShouldBeTrue();

    await reconnected.SendAsync(new GetLobbiesPacket());
    var lobbies = await reconnected.ExpectAsync<GetLobbiesResponsePacket>();

    var lobby = lobbies.Lobbies.SingleOrDefault(candidate => candidate.Id == lobbyId);
    lobby.ShouldNotBeNull();
    lobby.MaxPlayers.ShouldBe(2u);
    lobby.WorldSize.ShouldBe(TestGames.WORLD_SIZE);
    lobby.Players.Select(player => player.PlayerId).ShouldContain(accountId);

    // the seat is still the account's own: joining it again is refused as a duplicate, not accepted twice
    await reconnected.SendAsync(new JoinLobbyPacket { LobbyId = lobbyId, Tribe = 0 });
    (await reconnected.ExpectAsync<JoinLobbyResponsePacket>()).Result.ShouldBe(LobbyActionResult.AlreadyJoinedLobby);
  }

  [Fact]
  public async Task TestOneAccountCanSitInSeveralLobbies() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);

    using var alice = await TestClient.ConnectAsync(server);
    await alice.RegisterAsync(TestGames.UniqueName("gina"), TestGames.PASSWORD);
    using var bob = await TestClient.ConnectAsync(server);
    await bob.RegisterAsync(TestGames.UniqueName("hank"), TestGames.PASSWORD);

    // one lobby alice creates herself...
    await alice.SendAsync(new CreateLobbyPacket { MaxPlayers = 2, WorldSize = TestGames.WORLD_SIZE, Tribe = 0 });
    var own = await alice.ExpectAsync<CreateLobbyResponsePacket>();
    own.Result.ShouldBe(LobbyActionResult.Ok);

    // ...and one somebody else created
    await bob.SendAsync(new CreateLobbyPacket { MaxPlayers = 2, WorldSize = TestGames.WORLD_SIZE, Tribe = 0 });
    var other = await bob.ExpectAsync<CreateLobbyResponsePacket>();
    other.Result.ShouldBe(LobbyActionResult.Ok);
    other.LobbyId.ShouldNotBe(own.LobbyId);

    await alice.SendAsync(new JoinLobbyPacket { LobbyId = other.LobbyId, Tribe = 0 });
    (await alice.ExpectAsync<JoinLobbyResponsePacket>()).Result.ShouldBe(LobbyActionResult.Ok);

    await alice.SendAsync(new GetLobbiesPacket());
    var lobbies = await alice.ExpectAsync<GetLobbiesResponsePacket>();
    lobbies.Lobbies.Count(lobby => lobby[alice.AccountId] != null).ShouldBe(2);

    // leaving one of them doesn't touch the other
    await alice.SendAsync(new LeaveLobbyPacket { LobbyId = other.LobbyId });
    (await alice.ExpectAsync<LeaveLobbyResponsePacket>()).Result.ShouldBe(LobbyActionResult.Ok);

    await alice.SendAsync(new GetLobbiesPacket());
    var afterLeaving = await alice.ExpectAsync<GetLobbiesResponsePacket>();
    afterLeaving.Lobbies.Count(lobby => lobby[alice.AccountId] != null).ShouldBe(1);
    afterLeaving.Lobbies.Single(lobby => lobby[alice.AccountId] != null).Id.ShouldBe(own.LobbyId);
  }

  #endregion

  #region Games

  [Fact]
  public async Task TestTwoPlayerGameStartsAndKeepsRunning() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);

    using var alice = await TestClient.ConnectAsync(server);
    var aliceName = TestGames.UniqueName("iris");
    await alice.RegisterAsync(aliceName, TestGames.PASSWORD);
    using var bob = await TestClient.ConnectAsync(server);
    var bobName = TestGames.UniqueName("jack");
    await bob.RegisterAsync(bobName, TestGames.PASSWORD);

    var gameId = await TestGames.StartGameAsync(alice, bob);

    var state = await alice.GetStateAsync(gameId);
    state.Result.ShouldBe(GameActionResult.Ok);
    state.WorldSize.ShouldBe(TestGames.WORLD_SIZE);
    state.Over.ShouldBeFalse();
    state.CurrentPlayer.ShouldBe(1u);
    state.Players.Count.ShouldBe(2);
    state.Players.Single(player => player.Name == aliceName).PlayerId.ShouldBe(1u);
    state.Players.Single(player => player.Name == bobName).PlayerId.ShouldBe(2u);
    state.Players.ShouldAllBe(player => player.Alive);
    state.Tiles.Length.ShouldBe((int)(TestGames.WORLD_SIZE * TestGames.WORLD_SIZE));

    // the lobby is gone, its game replaced it
    await alice.SendAsync(new GetLobbiesPacket());
    (await alice.ExpectAsync<GetLobbiesResponsePacket>()).Lobbies.ShouldNotContain(lobby => lobby.Id == gameId);

    // a daily game must survive a few ticks of the start-lobbies loop without expiring on its own
    (await alice.TryReceiveAsync<GameOverPacket>(null, TimeSpan.FromSeconds(7))).ShouldBeNull();
    (await alice.TryReceiveAsync<TurnStartedPacket>(null, TimeSpan.Zero)).ShouldBeNull();
    (await alice.GetStateAsync(gameId)).Turn.ShouldBe(state.Turn);
  }

  [Fact]
  public async Task TestGameJoinIsRefusedToNonMembers() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);

    using var alice = await TestClient.ConnectAsync(server);
    await alice.RegisterAsync(TestGames.UniqueName("kate"), TestGames.PASSWORD);
    using var bob = await TestClient.ConnectAsync(server);
    await bob.RegisterAsync(TestGames.UniqueName("liam"), TestGames.PASSWORD);

    var gameId = await TestGames.StartGameAsync(alice, bob);

    using var stranger = await TestClient.ConnectAsync(server);
    await stranger.RegisterAsync(TestGames.UniqueName("mallory"), TestGames.PASSWORD);

    await stranger.SendAsync(new JoinGamePacket { GameId = gameId });
    var refused = await stranger.ExpectAsync<GameStatePacket>();
    refused.Result.ShouldBe(GameActionResult.NotInGame);
    refused.Tiles.ShouldBeEmpty();

    await stranger.SendAsync(new JoinGamePacket { GameId = gameId + 1000 });
    (await stranger.ExpectAsync<GameStatePacket>()).Result.ShouldBe(GameActionResult.GameNotFound);

    // no membership means no state and no actions either
    (await stranger.GetStateAsync(gameId)).Result.ShouldBe(GameActionResult.NotInGame);

    await stranger.SendAsync(new EndTurnPacket { GameId = gameId });
    (await stranger.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldBe(GameActionResult.NotInGame);

    await stranger.SendAsync(new ResignGamePacket { GameId = gameId });
    (await stranger.ExpectAsync<MembershipResultPacket>()).Result.ShouldBe(GameActionResult.NotInGame);

    await stranger.SendAsync(new GetMyGamesPacket());
    (await stranger.ExpectAsync<MyGamesPacket>()).GameIds.ShouldBeEmpty();
  }

  [Fact]
  public async Task TestLeavingAGameKeepsTheSeatAndRejoiningRestoresTheState() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);

    using var alice = await TestClient.ConnectAsync(server);
    await alice.RegisterAsync(TestGames.UniqueName("nina"), TestGames.PASSWORD);
    using var bob = await TestClient.ConnectAsync(server);
    await bob.RegisterAsync(TestGames.UniqueName("oscar"), TestGames.PASSWORD);

    var gameId = await TestGames.StartGameAsync(alice, bob);
    var before = await alice.GetStateAsync(gameId);

    await alice.SendAsync(new LeaveGamePacket { GameId = gameId });
    var left = await alice.ExpectAsync<MembershipResultPacket>();
    left.Result.ShouldBe(GameActionResult.Ok);
    left.GameId.ShouldBe(gameId);

    // leaving is not resigning: the player is still alive and still owns the game
    await alice.SendAsync(new GetMyGamesPacket());
    (await alice.ExpectAsync<MyGamesPacket>()).GameIds.ShouldContain(gameId);

    // ...but the view is closed, so gameplay packets bound to the transport don't resolve anymore
    (await alice.GetStateAsync(gameId)).Result.ShouldBe(GameActionResult.NotInGame);

    await alice.SendAsync(new JoinGamePacket { GameId = gameId });
    var rejoined = await alice.ExpectAsync<GameStatePacket>(packet => packet.Result == GameActionResult.Ok);
    rejoined.GameId.ShouldBe(gameId);
    rejoined.Turn.ShouldBe(before.Turn);
    rejoined.CurrentPlayer.ShouldBe(before.CurrentPlayer);
    rejoined.Tiles.ShouldBe(before.Tiles);
    rejoined.Troops.ShouldBe(before.Troops);
    rejoined.Players.Single(player => player.PlayerId == 1).Alive.ShouldBeTrue();

    // and the seat works again
    await alice.SendAsync(new EndTurnPacket { GameId = gameId });
    (await alice.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldBe(GameActionResult.Ok);
  }

  [Fact]
  public async Task TestAnAccountKeepsOneSeatPerGame() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);

    using var alice = await TestClient.ConnectAsync(server);
    var aliceName = TestGames.UniqueName("peggy");
    await alice.RegisterAsync(aliceName, TestGames.PASSWORD);
    using var bob = await TestClient.ConnectAsync(server);
    var bobName = TestGames.UniqueName("quinn");
    await bob.RegisterAsync(bobName, TestGames.PASSWORD);

    // alice hosts the first game, so she's player 1 there...
    var first = await TestGames.StartGameAsync(alice, bob);
    // ...and joins the second one, where she's player 2 instead
    var second = await TestGames.StartGameAsync(bob, alice);
    second.ShouldNotBe(first);

    await alice.SendAsync(new GetMyGamesPacket());
    var mine = await alice.ExpectAsync<MyGamesPacket>();
    mine.GameIds.OrderBy(id => id).ShouldBe(new[] { first, second }.OrderBy(id => id));

    var firstState = await alice.GetStateAsync(first);
    firstState.Players.Single(player => player.Name == aliceName).PlayerId.ShouldBe(1u);
    var secondState = await alice.GetStateAsync(second);
    secondState.Players.Single(player => player.Name == aliceName).PlayerId.ShouldBe(2u);

    // both games open the first turn on their own player 1, and the mapping decides who may act
    await alice.SendAsync(new EndTurnPacket { GameId = second });
    (await alice.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldBe(GameActionResult.NotYourTurn);

    await alice.SendAsync(new EndTurnPacket { GameId = first });
    (await alice.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldBe(GameActionResult.Ok);

    // the turn only moved in the game it was sent for
    var turnStarted = await bob.ExpectAsync<TurnStartedPacket>(packet => packet.GameId == first);
    turnStarted.PlayerId.ShouldBe(2u);
    (await alice.GetStateAsync(second)).CurrentPlayer.ShouldBe(1u);
  }

  [Fact]
  public async Task TestMovesAndTurnsSurviveAServerRestart() {
    using var database = new TempDatabase();
    var aliceName = TestGames.UniqueName("rita");
    var bobName = TestGames.UniqueName("sam");

    ulong gameId;
    string aliceToken;
    string bobToken;
    uint movedTurn;
    Vector2I destination;

    await using (var first = await TestServer.StartAsync(database.Path)) {
      using var alice = await TestClient.ConnectAsync(first);
      aliceToken = (await alice.RegisterAsync(aliceName, TestGames.PASSWORD)).Token;
      using var bob = await TestClient.ConnectAsync(first);
      bobToken = (await bob.RegisterAsync(bobName, TestGames.PASSWORD)).Token;

      gameId = await TestGames.StartGameAsync(alice, bob);
      var state = await alice.GetStateAsync(gameId);

      // move any troop of player 1 to any tile it accepts: the point is that the move sticks, not which one it is
      destination = await MoveAnyTroopAsync(alice, bob, gameId, state);
      movedTurn = state.Turn;

      await alice.SendAsync(new EndTurnPacket { GameId = gameId });
      (await alice.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldBe(GameActionResult.Ok);
      var started = await alice.ExpectAsync<TurnStartedPacket>(packet => packet.GameId == gameId);
      started.PlayerId.ShouldBe(2u);
    }

    await using var second = await TestServer.StartAsync(database.Path);

    using var reconnected = await TestClient.ConnectAsync(second);
    (await reconnected.ResumeAsync(aliceToken)).Ok.ShouldBeTrue();

    // the game itself survived, and so did the membership
    await reconnected.SendAsync(new GetMyGamesPacket());
    (await reconnected.ExpectAsync<MyGamesPacket>()).GameIds.ShouldContain(gameId);

    await reconnected.SendAsync(new JoinGamePacket { GameId = gameId });
    var restored = await reconnected.ExpectAsync<GameStatePacket>(packet => packet.Result == GameActionResult.Ok);

    restored.GameId.ShouldBe(gameId);
    restored.CurrentPlayer.ShouldBe(2u);
    restored.Turn.ShouldBeGreaterThanOrEqualTo(movedTurn);
    restored.TroopAt(destination).Player.ShouldBe(1u);
    restored.Players.Count.ShouldBe(2);
    restored.Players.ShouldAllBe(player => player.Alive);

    // and the restored turn order is enforced against the restored seats
    await reconnected.SendAsync(new EndTurnPacket { GameId = gameId });
    (await reconnected.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldBe(GameActionResult.NotYourTurn);

    using var bobAgain = await TestClient.ConnectAsync(second);
    (await bobAgain.ResumeAsync(bobToken)).Ok.ShouldBeTrue();
    await bobAgain.SendAsync(new JoinGamePacket { GameId = gameId });
    (await bobAgain.ExpectAsync<GameStatePacket>(packet => packet.Result == GameActionResult.Ok)).CurrentPlayer
      .ShouldBe(2u);

    await bobAgain.SendAsync(new EndTurnPacket { GameId = gameId });
    (await bobAgain.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldBe(GameActionResult.Ok);
  }

  [Fact]
  public async Task TestResignationEliminatesThePlayerAndEndsTheGame() {
    using var database = new TempDatabase();
    await using var server = await TestServer.StartAsync(database.Path);

    using var alice = await TestClient.ConnectAsync(server);
    await alice.RegisterAsync(TestGames.UniqueName("tina"), TestGames.PASSWORD);
    using var bob = await TestClient.ConnectAsync(server);
    await bob.RegisterAsync(TestGames.UniqueName("user"), TestGames.PASSWORD);

    var gameId = await TestGames.StartGameAsync(alice, bob);

    await bob.SendAsync(new ResignGamePacket { GameId = gameId });
    var resigned = await bob.ExpectAsync<MembershipResultPacket>();
    resigned.Result.ShouldBe(GameActionResult.Ok);
    resigned.GameId.ShouldBe(gameId);

    // the other player learns about it, and about the game ending with him as the winner
    var eliminated = await alice.ExpectAsync<PlayerEliminatedPacket>(packet => packet.GameId == gameId);
    eliminated.PlayerId.ShouldBe(2u);

    var over = await alice.ExpectAsync<GameOverPacket>(packet => packet.GameId == gameId);
    over.Winner.ShouldBe(1u);
    over.Players.Single(player => player.PlayerId == 2).Alive.ShouldBeFalse();
    over.Players.Single(player => player.PlayerId == 1).Alive.ShouldBeTrue();

    // resigning twice isn't allowed, and a finished game refuses gameplay
    await bob.SendAsync(new ResignGamePacket { GameId = gameId });
    (await bob.ExpectAsync<MembershipResultPacket>()).Result.ShouldNotBe(GameActionResult.Ok);

    await alice.SendAsync(new EndTurnPacket { GameId = gameId });
    (await alice.ExpectAsync<EndTurnResponsePacket>()).Result.ShouldNotBe(GameActionResult.Ok);

    // the finished game is retained so its members can still look at the result
    var final = await alice.GetStateAsync(gameId);
    final.Over.ShouldBeTrue();
    final.Winner.ShouldBe(1u);

    await alice.SendAsync(new GetMyGamesPacket());
    (await alice.ExpectAsync<MyGamesPacket>()).GameIds.ShouldContain(gameId);
  }

  #endregion

  /// <summary>
  /// Moves any troop of player 1 onto any neighboring tile the engine accepts
  /// </summary>
  /// <remarks>
  /// The world is generated from a random seed, so which tiles a troop may step on isn't known upfront: the helper
  /// tries the neighbors until one is accepted, and asserts the resulting broadcast
  /// </remarks>
  /// <returns>the position the troop ended up on</returns>
  private static async Task<Vector2I> MoveAnyTroopAsync(TestClient mover, TestClient observer, ulong gameId,
    GameStatePacket state) {
    var size = (int)state.WorldSize;

    foreach (var from in state.TroopsOf(1)) {
      for (var dy = -1; dy <= 1; dy++) {
        for (var dx = -1; dx <= 1; dx++) {
          var to = new Vector2I(from.X + dx, from.Y + dy);
          if ((dx == 0 && dy == 0) || to.X < 0 || to.Y < 0 || to.X >= size || to.Y >= size) {
            continue;
          }

          await mover.SendAsync(new MoveTroopPacket { GameId = gameId, From = from, To = to });
          if ((await mover.ExpectAsync<MoveTroopResponsePacket>()).Result != GameActionResult.Ok) {
            continue;
          }

          // both players see the move, with the same positions the mover asked for
          foreach (var client in new[] { mover, observer }) {
            var broadcast = await client.ExpectAsync<TroopMovedPacket>(packet => packet.GameId == gameId);
            broadcast.PlayerId.ShouldBe(1u);
            broadcast.From.ShouldBe(from);
            broadcast.To.ShouldBe(to);
            broadcast.Update.Tiles.ShouldNotBeEmpty();
          }

          (await mover.GetStateAsync(gameId)).TroopAt(to).Player.ShouldBe(1u);
          return to;
        }
      }
    }

    throw new Xunit.Sdk.XunitException("no troop of player 1 could move anywhere on the generated world");
  }
}
