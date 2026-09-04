namespace OpenPolytopia;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Gameplay;
using Common.Network.Packets;
using Godot;
using Server;
using Shouldly;

public class GameSessionTest {
  // fixed so every test generates the same world, keeping the tests deterministic
  private const int SEED = 5678;
  private const uint WORLD_SIZE = 11;

  private static readonly GameData _data = GameData.LoadEmbedded();

  private readonly GameManager _gameManager = new();

  private static LobbyData NewLobby() {
    var players = new List<LobbyPlayerData> {
      new() { PlayerId = 100, Name = "One", Tribe = 0 },
      new() { PlayerId = 101, Name = "Two", Tribe = 0 }
    };

    return new LobbyData { Id = 3, MaxPlayers = 2, WorldSize = WORLD_SIZE, Players = players };
  }

  private Task<GameSession> CreateGameAsync() => _gameManager.CreateGameAsync(NewLobby(), _data, SEED);

  private static T RoundTrip<T>(T packet) where T : IPacket, new() {
    List<byte> bytes = [];
    packet.Serialize(bytes);
    var deserialized = new T();
    var index = 0u;
    deserialized.Deserialize(bytes.ToArray(), ref index);
    index.ShouldBe((uint)bytes.Count);
    return deserialized;
  }

  [Fact]
  public async Task TestTakeUpdateEmptyRightAfterCreation() {
    var session = await CreateGameAsync();

    // Start() already happened before the session was built, so the very first diff is empty
    session.TakeUpdate().Tiles.ShouldBeEmpty();
  }

  [Fact]
  public async Task TestTakeUpdateReportsOnlyTheChangedTilesAfterAMove() {
    var session = await CreateGameAsync();
    session.TakeUpdate();

    var player = session.Game.Players[0];
    var capitalId = session.Game.CityIdsOf(player.Id).First();
    var capital = session.Game.Grid.IndexToGridPosition(session.Game.Cities.GetIndex(capitalId));

    var reachable = session.Game.ReachableTiles(player.Id, capital);
    var target = reachable.First(position => position != capital);

    session.Game.MoveTroop(player.Id, capital, target).ShouldBe(GameActionResult.Ok);

    var update = session.TakeUpdate();

    // only the origin and destination tiles carry a troop change; nothing else moved
    update.Tiles.Count.ShouldBe(2);
    var changedIndexes = update.Tiles.Select(tile => tile.Index).ToHashSet();
    changedIndexes.ShouldContain(session.Game.Grid.GridPositionToIndex(capital));
    changedIndexes.ShouldContain(session.Game.Grid.GridPositionToIndex(target));
  }

  [Fact]
  public async Task TestPlayerIdOfUnknownConnectionIsZero() {
    var session = await CreateGameAsync();

    session.PlayerIdOf(999).ShouldBe(0);
  }

  [Fact]
  public async Task TestBuildStateRoundTripsThroughSerialization() {
    var session = await CreateGameAsync();

    var packet = RoundTrip(session.BuildState());

    packet.Result.ShouldBe(GameActionResult.Ok);
    packet.GameId.ShouldBe(session.Id);
    packet.WorldSize.ShouldBe(WORLD_SIZE);
    packet.Turn.ShouldBe(session.Game.Turn);
    packet.CurrentPlayer.ShouldBe((uint)session.Game.CurrentPlayer);
    packet.Players.Count.ShouldBe(session.Game.Players.Count);
    packet.Tiles.Length.ShouldBe((int)(WORLD_SIZE * WORLD_SIZE));
    packet.Troops.Length.ShouldBe((int)(WORLD_SIZE * WORLD_SIZE));
  }

  [Fact]
  public async Task TestBuildStateDoesNotAffectTakeUpdateSnapshot() {
    var session = await CreateGameAsync();

    // BuildState must never consume the diff TakeUpdate reports
    session.BuildState();
    session.TakeUpdate().Tiles.ShouldBeEmpty();
  }
}
