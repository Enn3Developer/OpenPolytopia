namespace OpenPolytopia;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Gameplay;
using Server;
using Shouldly;

public class GameManagerTest {
  // fixed so every test generates the same world, keeping the tests deterministic
  private const int SEED = 1234;
  private const uint WORLD_SIZE = 11;

  private static readonly GameData _data = GameData.LoadEmbedded();

  private readonly GameManager _gameManager = new();

  private static LobbyData NewLobby(int players = 2, uint worldSize = WORLD_SIZE) {
    var lobbyPlayers = new List<LobbyPlayerData>(players);
    for (var i = 0; i < players; i++) {
      lobbyPlayers.Add(new LobbyPlayerData { PlayerId = (uint)(100 + i), Name = $"Player{i}", Tribe = 0 });
    }

    return new LobbyData { Id = 7, MaxPlayers = (uint)players, WorldSize = worldSize, Players = lobbyPlayers };
  }

  private Task<GameSession> CreateGameAsync(LobbyData? lobby = null) =>
    _gameManager.CreateGameAsync(lobby ?? NewLobby(), _data, SEED);

  [Fact]
  public async Task TestCreateGameRegistersSessionAndMapsPlayers() {
    var lobby = NewLobby();
    var session = await CreateGameAsync(lobby);

    _gameManager[lobby.Id].ShouldBe(session);
    session.Id.ShouldBe(lobby.Id);
    session.Game.Grid.Size.ShouldBe(WORLD_SIZE);

    // players get ids 1..n in lobby order, mapped back to the connection id they joined with
    session.Connections[1].ShouldBe(lobby.Players[0].PlayerId);
    session.Connections[2].ShouldBe(lobby.Players[1].PlayerId);
    session.PlayerIdOf(lobby.Players[0].PlayerId).ShouldBe(1);
    session.PlayerIdOf(lobby.Players[1].PlayerId).ShouldBe(2);

    _gameManager.FindByConnection(lobby.Players[0].PlayerId).ShouldBe(session);
  }

  [Fact]
  public async Task TestCreateGameSpawnsAWarriorPerPlayer() {
    var session = await CreateGameAsync();

    foreach (var player in session.Game.Players) {
      var capitalId = session.Game.CityIdsOf(player.Id).First();
      var position = session.Game.Grid.IndexToGridPosition(session.Game.Cities.GetIndex(capitalId));
      var troop = session.Game.Troops[position];

      troop.IsValid().ShouldBeTrue();
      troop.Type.ShouldBe(TroopType.Warrior);
      troop.Player.ShouldBe((uint)player.Id);
    }
  }

  [Theory]
  [InlineData(1)]
  [InlineData(17)]
  public async Task TestCreateGameRejectsInvalidPlayerCount(int players) {
    var lobby = NewLobby(players);

    await Should.ThrowAsync<ArgumentException>(() => CreateGameAsync(lobby));
  }

  [Fact]
  public async Task TestRemovePlayerUnmapsAndReturnsSession() {
    var lobby = NewLobby();
    var session = await CreateGameAsync(lobby);

    _gameManager.RemovePlayer(lobby.Players[0].PlayerId, out var playerId).ShouldBe(session);
    playerId.ShouldBe(1);

    // unmapped everywhere: the connection stops receiving the packets of the game
    _gameManager.FindByConnection(lobby.Players[0].PlayerId).ShouldBeNull();
    session.PlayerIdOf(lobby.Players[0].PlayerId).ShouldBe(0);
    session.ConnectionIds.ShouldBe([lobby.Players[1].PlayerId]);
  }

  [Fact]
  public void TestRemovePlayerOfUnknownConnectionReturnsNull() =>
    _gameManager.RemovePlayer(999, out _).ShouldBeNull();

  [Fact]
  public async Task TestRemoveGameUnmapsEveryConnection() {
    var lobby = NewLobby();
    var session = await CreateGameAsync(lobby);

    _gameManager.RemoveGame(lobby.Id);

    _gameManager[lobby.Id].ShouldBeNull();
    foreach (var player in lobby.Players) {
      _gameManager.FindByConnection(player.PlayerId).ShouldBeNull();
    }
  }
}
