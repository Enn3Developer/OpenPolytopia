namespace OpenPolytopia;

using System;
using System.Collections.Generic;
using System.Linq;
using Common;
using Common.Gameplay;
using Godot;
using Shouldly;

/// <summary>
/// Roundtrip tests for <see cref="Game.ToSnapshot"/> and <see cref="Game.Restore"/>
/// </summary>
/// <remarks>
/// Every restore rebuilds the content from scratch, so a restored game never shares a manager, a tech tree or a
/// player state with the game the snapshot came from: anything that survives the roundtrip really was in the snapshot
/// </remarks>
public class GamePersistenceTest {
  /// <summary>
  /// The content a game is built from, rebuilt fresh for every restore
  /// </summary>
  private readonly record struct Content(TroopManager Troops, TribeManager Tribes, BuildingManager Buildings,
    TechTreeDefinition TechTree);

  private static Content NewContent(uint gridSize) {
    var troops = new TroopManager(gridSize);
    var tribes = new TribeManager();
    var buildings = new BuildingManager();

    troops.RegisterTroops(EmbeddedResources.LoadTroops().ShouldNotBeNull());
    tribes.RegisterTribes(EmbeddedResources.LoadTribes().ShouldNotBeNull());
    buildings.RegisterBuildings(EmbeddedResources.LoadBuildings().ShouldNotBeNull());

    return new Content(troops, tribes, buildings,
      TechTreeDefinition.FromSerializedData(EmbeddedResources.LoadTechTree().ShouldNotBeNull()));
  }

  private static Game Roundtrip(Game game) => Restore(game.ToSnapshot());

  private static Game Restore(GameSnapshot snapshot) {
    var content = NewContent(snapshot.GridSize);
    return Game.Restore(snapshot, content.Troops, content.Tribes, content.Buildings, content.TechTree);
  }

  /// <summary>
  /// Asserts two snapshots describe the very same game, member by member
  /// </summary>
  private static void ShouldMatch(GameSnapshot actual, GameSnapshot expected) {
    actual.Version.ShouldBe(expected.Version);
    actual.GridSize.ShouldBe(expected.GridSize);
    actual.Tiles.ShouldBe(expected.Tiles);
    actual.Troops.ShouldBe(expected.Troops);
    actual.CityIndexes.ShouldBe(expected.CityIndexes);
    actual.MaxTurns.ShouldBe(expected.MaxTurns);
    actual.Turn.ShouldBe(expected.Turn);
    actual.CurrentPlayer.ShouldBe(expected.CurrentPlayer);
    actual.Started.ShouldBe(expected.Started);
    actual.Over.ShouldBe(expected.Over);
    actual.Winner.ShouldBe(expected.Winner);

    actual.Players.Count.ShouldBe(expected.Players.Count);
    foreach (var (actualPlayer, expectedPlayer) in actual.Players.Zip(expected.Players)) {
      actualPlayer.Id.ShouldBe(expectedPlayer.Id);
      actualPlayer.Tribe.ShouldBe(expectedPlayer.Tribe);
      actualPlayer.Stars.ShouldBe(expectedPlayer.Stars);
      actualPlayer.Score.ShouldBe(expectedPlayer.Score);
      actualPlayer.Alive.ShouldBe(expectedPlayer.Alive);
      actualPlayer.ResearchedTechs.ShouldBe(expectedPlayer.ResearchedTechs);
    }
  }

  /// <summary>
  /// Builds a started game and plays a bit of everything on it: research, building, training, moving and fighting
  /// </summary>
  /// <remarks>
  /// Nothing in the engine is random, so the same sequence on two equal games always lands on the same state; that's
  /// what makes the "keep playing after a restore" tests meaningful
  /// </remarks>
  private static Game PlayedGame() {
    var game = GameTestFixture.NewStartedGame(3);

    // player 1 researches, harvests a fruit into its capital and trains a second troop
    game.Players[0].Stars = 40;
    game.ResearchTech(1, "farming").ShouldBe(GameActionResult.Ok);
    game.Grid.ModifyTile(GameTestFixture.P(0, 0), (ref Tile tile) => tile.Modifier = (int)FieldTileModifier.Fruit);
    game.Build(1, GameTestFixture.P(0, 0), BuildingType.Fruit).Result.ShouldBe(GameActionResult.Ok);
    game.MoveTroop(1, GameTestFixture.P(1, 1), GameTestFixture.P(2, 2)).ShouldBe(GameActionResult.Ok);
    game.TrainTroop(1, GameTestFixture.P(1, 1), TroopType.Warrior).ShouldBe(GameActionResult.Ok);
    game.EndTurn(1).Result.ShouldBe(GameActionResult.Ok);

    // player 2 walks its warrior out of its capital and takes a hit at nothing in particular
    game.Players[1].Stars = 25;
    game.MoveTroop(2, GameTestFixture.P(6, 1), GameTestFixture.P(5, 2)).ShouldBe(GameActionResult.Ok);
    game.EndTurn(2).Result.ShouldBe(GameActionResult.Ok);

    // player 3 keeps its stars and its troop where they are
    game.EndTurn(3).Result.ShouldBe(GameActionResult.Ok);

    return game;
  }

  [Fact]
  public void TestRoundtripOfAGameThatHasntStarted() {
    var game = GameTestFixture.NewGame();
    var snapshot = game.ToSnapshot();

    var restored = Roundtrip(game);

    restored.Started.ShouldBeFalse();
    restored.Turn.ShouldBe(0u);
    restored.CurrentPlayer.ShouldBe(0);
    ShouldMatch(restored.ToSnapshot(), snapshot);
  }

  [Fact]
  public void TestRoundtripPreservesTheWholeBoard() {
    var game = PlayedGame();
    var snapshot = game.ToSnapshot();

    var restored = Roundtrip(game);

    restored.Grid.Size.ShouldBe(game.Grid.Size);
    for (var index = 0u; index < game.Grid.Size * game.Grid.Size; index++) {
      restored.Grid[index].Raw.ShouldBe(game.Grid[index].Raw, $"tile {index} differs");
      restored.Troops[index].Raw.ShouldBe(game.Troops[index].Raw, $"troop {index} differs");
    }

    ShouldMatch(restored.ToSnapshot(), snapshot);
  }

  [Fact]
  public void TestRoundtripPreservesCityInternals() {
    var game = GameTestFixture.NewStartedGame();
    var cityId = game.CityIdsOf(1).Single();

    // every field of the packed city data, set to something that isn't its default
    game.Cities.ModifyCity(cityId, (ref CityData city) => {
      city.Level = 5;
      city.Population = 3;
      city.Troops = 7;
      city.Parks = 2;
      city.Wall = true;
      city.Forge = true;
      city.Connected = true;
    });

    var restored = Roundtrip(game);

    restored.Cities.Cities.ShouldBe(game.Cities.Cities);
    var city = restored.Cities[cityId];
    city.Owner.ShouldBe(1);
    city.Level.ShouldBe(5);
    city.Population.ShouldBe(3);
    city.Troops.ShouldBe(7);
    city.Parks.ShouldBe(2);
    city.Wall.ShouldBeTrue();
    city.Forge.ShouldBeTrue();
    city.Capital.ShouldBeTrue();
    city.Connected.ShouldBeTrue();
    city.Stars.ShouldBe(game.Cities[cityId].Stars);
  }

  [Fact]
  public void TestRoundtripPreservesEveryCityAndItsIndex() {
    var game = GameTestFixture.NewStartedGame(4);
    var village = game.Cities.RegisterCity(GameTestFixture.P(3, 3));
    game.Cities.ModifyCity(village, (ref CityData city) => city.Level = 2);

    var restored = Roundtrip(game);

    restored.Cities.Cities.Count.ShouldBe(5);
    for (var id = 1u; id <= 5; id++) {
      restored.Cities.GetIndex(id).ShouldBe(game.Cities.GetIndex(id));
      restored.Cities[id].ToULong().ShouldBe(game.Cities[id].ToULong());
    }

    restored.Cities[village].Level.ShouldBe(2);
    restored.Cities[village].Owner.ShouldBe(0);
  }

  [Fact]
  public void TestRoundtripPreservesTroopRawState() {
    var game = GameTestFixture.NewStartedGame();
    var position = GameTestFixture.P(3, 3);
    game.Troops.SpawnTroop(position, 1, 1, TroopType.Rider);
    game.Troops.SetVeteran(position);
    game.Troops.ModifyTroop(position, (ref TroopData troop) => {
      troop.Hp = 12;
      troop.Moved = true;
      troop.Attacked = true;
    });

    var restored = Roundtrip(game);
    var troop = restored.Troops[position];

    troop.IsValid().ShouldBeTrue();
    troop.Type.ShouldBe(TroopType.Rider);
    troop.Player.ShouldBe(1u);
    troop.City.ShouldBe(1u);
    troop.Hp.ShouldBe(12u);
    troop.Veteran.ShouldBeTrue();
    troop.Moved.ShouldBeTrue();
    troop.Attacked.ShouldBeTrue();
    restored.MaxHpOf(troop).ShouldBe(game.MaxHpOf(game.Troops[position]));
  }

  [Fact]
  public void TestRoundtripPreservesPlayersInTurnOrder() {
    var game = PlayedGame();

    var restored = Roundtrip(game);

    restored.Players.Count.ShouldBe(game.Players.Count);
    foreach (var (actual, expected) in restored.Players.Zip(game.Players)) {
      actual.Id.ShouldBe(expected.Id);
      actual.Tribe.ShouldBe(expected.Tribe);
      actual.Stars.ShouldBe(expected.Stars);
      actual.Alive.ShouldBe(expected.Alive);
      actual.Score.ScoreValue.ShouldBe(expected.Score.ScoreValue);
      restored[expected.Id].ShouldBe(actual);
    }
  }

  [Fact]
  public void TestRoundtripPreservesResearchedTechnology() {
    var game = GameTestFixture.NewStartedGame();
    game.Players[0].Stars = 100;
    game.ResearchTech(1, "riding").ShouldBe(GameActionResult.Ok);
    game.ResearchTech(1, "roads").ShouldBe(GameActionResult.Ok);

    var restored = Roundtrip(game);

    restored.Players[0].TechTree.ResearchedIds().ShouldBe(game.Players[0].TechTree.ResearchedIds());
    restored.Players[0].TechTree.HasResearched("riding").ShouldBeTrue();
    restored.Players[0].TechTree.HasResearched("roads").ShouldBeTrue();

    // the starting node of the tribe is restored because it was researched, not because a tribe hands it out
    restored.Players[0].TechTree.HasResearched("organization").ShouldBeTrue();
    restored.Players[0].TechTree.HasResearched("climbing").ShouldBeFalse();

    // player 2 never researched anything past its start, and researching for one player never touched the other
    restored.Players[1].TechTree.ResearchedIds().ShouldBe(game.Players[1].TechTree.ResearchedIds());
    restored.Players[1].TechTree.HasResearched("riding").ShouldBeFalse();
  }

  [Fact]
  public void TestRoundtripPreservesScoreAndSettingsAndTurnState() {
    var game = GameTestFixture.NewStartedGame(3, new GameSettings { MaxTurns = 12 });
    game.Players[1].Score.AddScore(ScoreType.MonumentsBuilt);
    game.Players[1].Score.AddScore(ScoreType.LoseCity(2));
    game.EndTurn(1).Result.ShouldBe(GameActionResult.Ok);

    var restored = Roundtrip(game);

    restored.Settings.MaxTurns.ShouldBe(12u);
    restored.Turn.ShouldBe(game.Turn);
    restored.CurrentPlayer.ShouldBe(2);
    restored.Started.ShouldBeTrue();
    restored.Over.ShouldBeFalse();
    restored.Winner.ShouldBe(0);
    restored.Players[1].Score.ScoreValue.ShouldBe(game.Players[1].Score.ScoreValue);
  }

  [Fact]
  public void TestRoundtripPreservesEliminatedPlayersAndAFinishedGame() {
    var game = GameTestFixture.NewStartedGame(3);
    game.Resign(2).Result.ShouldBe(GameActionResult.Ok);
    game.Resign(3).Result.ShouldBe(GameActionResult.Ok);

    game.Over.ShouldBeTrue();
    game.Winner.ShouldBe(1);

    var restored = Roundtrip(game);

    restored.Over.ShouldBeTrue();
    restored.Winner.ShouldBe(1);
    restored.Players[0].Alive.ShouldBeTrue();
    restored.Players[1].Alive.ShouldBeFalse();
    restored.Players[2].Alive.ShouldBeFalse();

    // a finished game stays finished: every action is refused exactly like on the original
    restored.EndTurn(1).Result.ShouldBe(GameActionResult.GameOver);
    ShouldMatch(restored.ToSnapshot(), game.ToSnapshot());
  }

  [Fact]
  public void TestSnapshotIsNotAViewOnTheLiveGame() {
    var game = GameTestFixture.NewStartedGame();
    var snapshot = game.ToSnapshot();

    game.Players[0].Stars = 999;
    game.Grid.ModifyTile(GameTestFixture.P(4, 4), (ref Tile tile) => tile.Kind = TileKind.Mountain);
    game.Troops.SpawnTroop(GameTestFixture.P(4, 4), 1, 1, TroopType.Warrior);
    game.Cities.RegisterCity(GameTestFixture.P(3, 3));

    var restored = Restore(snapshot);

    restored.Players[0].Stars.ShouldNotBe(999);
    restored.Grid[GameTestFixture.P(4, 4)].Kind.ShouldBe(TileKind.Field);
    restored.Troops[GameTestFixture.P(4, 4)].IsValid().ShouldBeFalse();
    restored.Cities.Cities.Count.ShouldBe(2);
  }

  [Fact]
  public void TestRestoredGameKeepsPlayingIdenticallyToTheOriginal() {
    var original = PlayedGame();
    var restored = Roundtrip(original);

    // the same sequence of actions, played on both, has to land on the same state
    foreach (var game in new[] { original, restored }) {
      game.CurrentPlayer.ShouldBe(1);
      game.Players[0].Stars = 30;
      // tier 2, unlocked by the farming PlayedGame researched: it only passes if the tech tree survived the restore
      game.ResearchTech(1, "construction").ShouldBe(GameActionResult.Ok);
      game.MoveTroop(1, GameTestFixture.P(2, 2), GameTestFixture.P(3, 3)).ShouldBe(GameActionResult.Ok);
      game.EndTurn(1).Result.ShouldBe(GameActionResult.Ok);

      game.MoveTroop(2, GameTestFixture.P(5, 2), GameTestFixture.P(4, 3)).ShouldBe(GameActionResult.Ok);
      game.EndTurn(2).Result.ShouldBe(GameActionResult.Ok);

      game.EndTurn(3).Result.ShouldBe(GameActionResult.Ok);
    }

    ShouldMatch(restored.ToSnapshot(), original.ToSnapshot());
  }

  [Fact]
  public void TestRestoredGameResolvesCombatIdenticallyToTheOriginal() {
    var original = GameTestFixture.NewStartedGame();

    // two troops of different players next to each other, outside anybody's capital
    original.Troops.SpawnTroop(GameTestFixture.P(3, 3), 1, 1, TroopType.Warrior);
    original.Troops.SpawnTroop(GameTestFixture.P(4, 3), 2, 2, TroopType.Defender);

    var restored = Roundtrip(original);

    var originalAttack = original.Attack(1, GameTestFixture.P(3, 3), GameTestFixture.P(4, 3));
    var restoredAttack = restored.Attack(1, GameTestFixture.P(3, 3), GameTestFixture.P(4, 3));

    originalAttack.Result.ShouldBe(GameActionResult.Ok);
    restoredAttack.ShouldBe(originalAttack);
    ShouldMatch(restored.ToSnapshot(), original.ToSnapshot());
  }

  [Fact]
  public void TestRestoredGameEndsOnTheTurnLimitLikeTheOriginal() {
    var game = GameTestFixture.NewStartedGame(2, new GameSettings { MaxTurns = 2 });
    game.EndTurn(1).Result.ShouldBe(GameActionResult.Ok);
    game.EndTurn(2).Turn.ShouldBe(2u);

    var restored = Roundtrip(game);

    restored.Settings.MaxTurns.ShouldBe(2u);
    restored.EndTurn(1).Result.ShouldBe(GameActionResult.Ok);

    var last = restored.EndTurn(2);
    last.GameOver.ShouldBeTrue();
    restored.Over.ShouldBeTrue();
    restored.Turn.ShouldBe(2u);
  }

  [Fact]
  public void TestRestoreRejectsASnapshotOfAnotherVersion() {
    var snapshot = GameTestFixture.NewStartedGame().ToSnapshot();
    var older = Clone(snapshot, version: GameSnapshot.CURRENT_VERSION - 1);

    Should.Throw<ArgumentException>(() => Restore(older));
  }

  [Fact]
  public void TestRestoreRejectsArraysThatDontMatchTheGrid() {
    var snapshot = GameTestFixture.NewStartedGame().ToSnapshot();

    Should.Throw<ArgumentException>(() => Restore(Clone(snapshot, tiles: [.. snapshot.Tiles.Skip(1)])));
    Should.Throw<ArgumentException>(() => Restore(Clone(snapshot, troops: [.. snapshot.Troops.Skip(1)])));
  }

  [Fact]
  public void TestRestoreRejectsATroopManagerSizedForAnotherGrid() {
    var snapshot = GameTestFixture.NewStartedGame().ToSnapshot();
    var content = NewContent(GameTestFixture.SIZE + 1);

    Should.Throw<ArgumentException>(() =>
      Game.Restore(snapshot, content.Troops, content.Tribes, content.Buildings, content.TechTree));
  }

  [Fact]
  public void TestRestoreRejectsACityOutsideTheGrid() {
    var snapshot = GameTestFixture.NewStartedGame().ToSnapshot();

    Should.Throw<ArgumentException>(() =>
      Restore(Clone(snapshot, cityIndexes: [GameTestFixture.SIZE * GameTestFixture.SIZE])));
  }

  [Fact]
  public void TestRestoreRejectsATechThatIsntInTheTree() {
    var snapshot = GameTestFixture.NewStartedGame().ToSnapshot();
    var players = snapshot.Players.Select(player => Clone(player, researched: ["not_a_tech"])).ToList();

    Should.Throw<ArgumentException>(() => Restore(Clone(snapshot, players: players)));
  }

  [Fact]
  public void TestRestoreRejectsAPlayerListThatIsntAGame() {
    var snapshot = GameTestFixture.NewStartedGame().ToSnapshot();

    Should.Throw<ArgumentException>(() => Restore(Clone(snapshot, players: [snapshot.Players[0]])));
    Should.Throw<ArgumentException>(() =>
      Restore(Clone(snapshot, players: [snapshot.Players[0], snapshot.Players[0]])));
    Should.Throw<ArgumentException>(() =>
      Restore(Clone(snapshot, players: [Clone(snapshot.Players[0], id: 17), snapshot.Players[1]])));
  }

  [Fact]
  public void TestRestoreRejectsATurnHeldByNobody() {
    var snapshot = GameTestFixture.NewStartedGame().ToSnapshot();

    Should.Throw<ArgumentException>(() => Restore(Clone(snapshot, currentPlayer: 9)));
  }

  [Fact]
  public void TestBlobRoundtripOfTheBoardArrays() {
    var snapshot = PlayedGame().ToSnapshot();

    var tiles = SnapshotEncoding.UnpackTiles(SnapshotEncoding.PackTiles(snapshot.Tiles));
    var troops = SnapshotEncoding.UnpackTroops(SnapshotEncoding.PackTroops(snapshot.Troops));
    var cityIndexes = SnapshotEncoding.UnpackCityIndexes(SnapshotEncoding.PackCityIndexes(snapshot.CityIndexes));

    tiles.ShouldBe(snapshot.Tiles);
    troops.ShouldBe(snapshot.Troops);
    cityIndexes.ShouldBe(snapshot.CityIndexes);

    // the blobs are the actual columns of a database row, so a game has to come back out of them
    var restored = Restore(Clone(snapshot, tiles: tiles, troops: troops, cityIndexes: cityIndexes));
    ShouldMatch(restored.ToSnapshot(), snapshot);
  }

  [Fact]
  public void TestBlobsAreLittleEndianAndSized() {
    SnapshotEncoding.PackTiles([0x0102030405060708]).ShouldBe(
      new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 });
    SnapshotEncoding.PackTroops([0x01020304]).ShouldBe(new byte[] { 0x04, 0x03, 0x02, 0x01 });

    SnapshotEncoding.PackTiles([1, 2, 3]).Length.ShouldBe(24);
    SnapshotEncoding.PackTroops([1, 2, 3]).Length.ShouldBe(12);
  }

  [Fact]
  public void TestBlobsRejectATruncatedColumn() {
    Should.Throw<ArgumentException>(() => SnapshotEncoding.UnpackTiles(new byte[7]));
    Should.Throw<ArgumentException>(() => SnapshotEncoding.UnpackTroops(new byte[3]));
    Should.Throw<ArgumentException>(() => SnapshotEncoding.UnpackCityIndexes(new byte[5]));
  }

  private static GameSnapshot Clone(GameSnapshot snapshot, int? version = null, ulong[]? tiles = null,
    uint[]? troops = null, uint[]? cityIndexes = null, IReadOnlyList<PlayerSnapshot>? players = null,
    int? currentPlayer = null) =>
    new() {
      Version = version ?? snapshot.Version,
      GridSize = snapshot.GridSize,
      Tiles = tiles ?? snapshot.Tiles,
      Troops = troops ?? snapshot.Troops,
      CityIndexes = cityIndexes ?? snapshot.CityIndexes,
      Players = players ?? snapshot.Players,
      MaxTurns = snapshot.MaxTurns,
      Turn = snapshot.Turn,
      CurrentPlayer = currentPlayer ?? snapshot.CurrentPlayer,
      Started = snapshot.Started,
      Over = snapshot.Over,
      Winner = snapshot.Winner
    };

  private static PlayerSnapshot Clone(PlayerSnapshot snapshot, int? id = null, IReadOnlyList<string>? researched = null) =>
    new() {
      Id = id ?? snapshot.Id,
      Tribe = snapshot.Tribe,
      Stars = snapshot.Stars,
      Score = snapshot.Score,
      Alive = snapshot.Alive,
      ResearchedTechs = researched ?? snapshot.ResearchedTechs
    };
}
