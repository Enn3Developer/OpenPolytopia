namespace OpenPolytopia;

using System.Linq;
using Common;
using Common.Gameplay;
using Godot;
using Shouldly;

public class GameEconomyTest {
  private static Vector2I CapitalPositionOf(Game game, int playerId) =>
    game.Grid.IndexToGridPosition(game.Cities.GetIndex(game.CityIdsOf(playerId).First()));

  [Fact]
  public void TestResearchLockedTierIsRefused() {
    var game = GameTestFixture.NewStartedGame();

    // archery (hunting branch, tier 1) needs hunting (tier 0), which Imperius hasn't researched
    game.ResearchTech(1, "archery").ShouldBe(GameActionResult.TechLocked);
  }

  [Fact]
  public void TestResearchCostGrowsWithOwnedCities() {
    var oneCity = GameTestFixture.NewStartedGame();
    oneCity.Players[0].Stars = 100;
    var before1 = oneCity.Players[0].Stars;
    oneCity.ResearchTech(1, "farming").ShouldBe(GameActionResult.Ok);
    var cost1 = before1 - oneCity.Players[0].Stars;

    var twoCities = GameTestFixture.NewStartedGame();
    var secondCityId = twoCities.Cities.RegisterCity(GameTestFixture.P(4, 4));
    twoCities.Cities.ModifyCity(secondCityId, (ref CityData c) => c.Owner = 1);
    twoCities.Players[0].Stars = 100;
    var before2 = twoCities.Players[0].Stars;
    twoCities.ResearchTech(1, "farming").ShouldBe(GameActionResult.Ok);
    var cost2 = before2 - twoCities.Players[0].Stars;

    cost2.ShouldBeGreaterThan(cost1);
  }

  [Fact]
  public void TestResearchDeductsStarsAndScoresDiscoveredTech() {
    var game = GameTestFixture.NewStartedGame();
    var starsBefore = game.Players[0].Stars;
    var scoreBefore = game.Players[0].Score.ScoreValue;

    // farming is tier 1 with 1 owned city: cost = 1 * (1 + 1) + 4 = 6
    game.ResearchTech(1, "farming").ShouldBe(GameActionResult.Ok);

    game.Players[0].Stars.ShouldBe(starsBefore - 6);
    game.Players[0].Score.ScoreValue.ShouldBe(scoreBefore + ScoreType.DiscoveredNewTech.ToInt());
  }

  [Fact]
  public void TestHarvestFruitClearsModifierAndAddsPopulation() {
    var game = GameTestFixture.NewStartedGame();
    var pos = GameTestFixture.P(0, 0); // inside player 1's capital territory
    game.Grid.ModifyTile(pos, (ref Tile t) => t.Modifier = (int)FieldTileModifier.Fruit);

    // Imperius already starts with "organization", which fruit harvesting needs
    var result = game.Build(1, pos, BuildingType.Fruit);

    result.Result.ShouldBe(GameActionResult.Ok);
    result.Population.ShouldBe(1u);
    game.Grid[pos].Modifier.ShouldBe(0);
  }

  [Fact]
  public void TestFarmRequiresCropResource() {
    var game = GameTestFixture.NewStartedGame();
    var pos = GameTestFixture.P(0, 0);

    game.Build(1, pos, BuildingType.Farm).Result.ShouldBe(GameActionResult.MissingResource);
  }

  [Fact]
  public void TestBuildingOutsideOwnTerritoryIsRefused() {
    var game = GameTestFixture.NewStartedGame();
    var pos = GameTestFixture.P(4, 4); // unclaimed, no owner
    game.Grid.ModifyTile(pos, (ref Tile t) => t.Modifier = (int)FieldTileModifier.Fruit);

    game.Build(1, pos, BuildingType.Fruit).Result.ShouldBe(GameActionResult.TileNotOwned);
  }

  [Fact]
  public void TestBuildRequiresTech() {
    var game = GameTestFixture.NewStartedGame();

    // windmill needs "construction", which Imperius hasn't researched
    game.Build(1, GameTestFixture.P(0, 0), BuildingType.Windmill).Result.ShouldBe(GameActionResult.TechLocked);
  }

  [Fact]
  public void TestWindmillCountsAdjacentFarmsAndLaterFarmFeedsIt() {
    var game = GameTestFixture.NewStartedGame();
    game.Players[0].Stars = 100;
    var capitalId = game.CityIdsOf(1).First();
    var farm1Pos = GameTestFixture.P(0, 0);
    var windmillPos = GameTestFixture.P(1, 0);
    var farm2Pos = GameTestFixture.P(2, 0);

    // push the level-up threshold out of reach so the population additions below can be read directly
    game.Cities.ModifyCity(capitalId, (ref CityData c) => c.Level = 10);
    game.Grid.ModifyTile(farm1Pos, (ref Tile t) => t.Modifier = (int)FieldTileModifier.Crop);
    game.Grid.ModifyTile(farm2Pos, (ref Tile t) => t.Modifier = (int)FieldTileModifier.Crop);
    game[1]!.TechTree.Research("farming");
    game[1]!.TechTree.Research("construction");

    game.Build(1, farm1Pos, BuildingType.Farm).Result.ShouldBe(GameActionResult.Ok); // +2 population

    var windmillResult = game.Build(1, windmillPos, BuildingType.Windmill);
    windmillResult.Result.ShouldBe(GameActionResult.Ok);
    windmillResult.Population.ShouldBe(1u); // one adjacent farm already stands there
    game.Cities[capitalId].Population.ShouldBe(3); // 2 (farm1) + 1 (windmill's adjacency)

    // a farm built later next to the windmill feeds it too, on top of its own population
    game.Build(1, farm2Pos, BuildingType.Farm).Result.ShouldBe(GameActionResult.Ok);
    game.Cities[capitalId].Population.ShouldBe(6); // 3 + 2 (farm2's own) + 1 (fed to the windmill)
  }

  [Fact]
  public void TestUniqueBuildingCantBeBuiltTwicePerCity() {
    var game = GameTestFixture.NewStartedGame();
    game.Players[0].Stars = 100;
    game[1]!.TechTree.Research("construction");

    game.Build(1, GameTestFixture.P(0, 0), BuildingType.Windmill).Result.ShouldBe(GameActionResult.Ok);
    game.Build(1, GameTestFixture.P(2, 0), BuildingType.Windmill).Result
      .ShouldBe(GameActionResult.BuildingAlreadyInCity);
  }

  [Fact]
  public void TestLevelUpCarriesOverflowPopulationAndScoresPerLevel() {
    var game = GameTestFixture.NewStartedGame();
    var capitalId = game.CityIdsOf(1).First();
    var scoreBefore = game.Players[0].Score.ScoreValue;

    var fruitPos = GameTestFixture.P(0, 0);
    game.Grid.ModifyTile(fruitPos, (ref Tile t) => t.Modifier = (int)FieldTileModifier.Fruit);
    game.Build(1, fruitPos, BuildingType.Fruit).Result.ShouldBe(GameActionResult.Ok); // population 0 -> 1, max is 2

    var farmPos = GameTestFixture.P(2, 0);
    game.Grid.ModifyTile(farmPos, (ref Tile t) => t.Modifier = (int)FieldTileModifier.Crop);
    game[1]!.TechTree.Research("farming");

    // population 1 + 2 = 3 overflows the level-1 max of 2 by exactly 1
    var result = game.Build(1, farmPos, BuildingType.Farm);

    result.Result.ShouldBe(GameActionResult.Ok);
    result.LevelsGained.ShouldBe(1u);
    game.Cities[capitalId].Level.ShouldBe(2);
    game.Cities[capitalId].Population.ShouldBe(1); // overflow carried over, not reset to 0
    game.Players[0].Score.ScoreValue.ShouldBe(scoreBefore + ScoreType.CityLevelUp(1).ToInt());
  }

  [Fact]
  public void TestRoadOnNeutralOkOnEnemyRefusedOnCityRefused() {
    var game = GameTestFixture.NewStartedGame();
    game[1]!.TechTree.Research("roads");

    var neutralPos = GameTestFixture.P(4, 4); // outside every player's territory
    game.Build(1, neutralPos, BuildingType.Road).Result.ShouldBe(GameActionResult.Ok);
    game.Grid[neutralPos].Roads.ShouldBeTrue();

    var enemyPos = CapitalPositionOf(game, 2) + new Vector2I(1, 0); // inside player 2's territory
    game.Build(1, enemyPos, BuildingType.Road).Result.ShouldBe(GameActionResult.TileNotOwned);

    game.Build(1, CapitalPositionOf(game, 1), BuildingType.Road).Result.ShouldBe(GameActionResult.WrongTile);
  }

  [Fact]
  public void TestCaptureVillageClaimsTerritoryButLeavesOtherOwnedTilesAlone() {
    var game = GameTestFixture.NewStartedGame();
    var villagePos = GameTestFixture.P(4, 4); // clear of every capital's 3x3 territory
    var villageId = game.Cities.RegisterCity(villagePos);
    game.Grid.ModifyTile(villagePos, (ref Tile t) => t.Kind = TileKind.Village);

    var otherOwnedPos = villagePos + new Vector2I(-1, -1);
    game.Grid.ModifyTile(otherOwnedPos, (ref Tile t) => t.Owner = 2);

    game.Troops.SpawnTroop(villagePos, 1, 0, TroopType.Warrior);

    var result = game.Capture(1, villagePos);

    result.Result.ShouldBe(GameActionResult.Ok);
    result.CityId.ShouldBe(villageId);
    result.PreviousOwner.ShouldBe(0);
    result.ClaimedTiles.ShouldBe(7u); // 8 neighbors minus the one already owned by player 2

    game.Cities[villageId].Owner.ShouldBe(1);
    game.Cities[villageId].Level.ShouldBe(1);
    game.Grid[villagePos].Modifier.ShouldBe((int)VillageTileModifier.City);
    game.Grid[otherOwnedPos].Owner.ShouldBe(2); // untouched
    game.Grid[otherOwnedPos].City.ShouldBe(0);
    game.Grid[villagePos + new Vector2I(1, 1)].City.ShouldBe((int)villageId); // newly claimed
    game.Troops[villagePos].Moved.ShouldBeTrue();
    game.Troops[villagePos].Attacked.ShouldBeTrue();
  }

  [Fact]
  public void TestCaptureRequiresATroopThatHasNotActed() {
    var game = GameTestFixture.NewStartedGame();
    var villagePos = GameTestFixture.P(4, 4);
    game.Cities.RegisterCity(villagePos);
    game.Grid.ModifyTile(villagePos, (ref Tile t) => t.Kind = TileKind.Village);
    game.Troops.SpawnTroop(villagePos, 1, 0, TroopType.Warrior);
    game.Troops.ModifyTroop(villagePos, (ref TroopData d) => d.Moved = true);

    game.Capture(1, villagePos).Result.ShouldBe(GameActionResult.TroopAlreadyActed);
  }

  [Fact]
  public void TestCapturingLastEnemyCityEliminatesAndEndsGame() {
    var game = GameTestFixture.NewStartedGame();
    var enemyCityId = game.CityIdsOf(2).First();
    var enemyCapitalPos = CapitalPositionOf(game, 2);
    game.Troops.DeleteTroop(enemyCapitalPos); // remove the defending warrior, combat is out of scope here
    game.Troops.SpawnTroop(enemyCapitalPos, 1, 0, TroopType.Warrior);

    // give player 2 a positive score first: Score.ScoreValue never goes negative, LoseCity alone would be invisible
    game[2]!.Score.AddScore(ScoreType.VillageConquered);
    var scoreBefore = game[2]!.Score.ScoreValue;

    var result = game.Capture(1, enemyCapitalPos);

    result.Result.ShouldBe(GameActionResult.Ok);
    result.PreviousOwner.ShouldBe(2);
    result.EliminatedPlayer.ShouldBe(2);
    game.Cities[enemyCityId].Owner.ShouldBe(1);
    game[2]!.Alive.ShouldBeFalse();
    game[2]!.Score.ScoreValue.ShouldBeLessThan(scoreBefore);
    game.Troops.Troops().Any(entry => entry.Troop.Player == 2).ShouldBeFalse();
    game.Over.ShouldBeTrue();
    game.Winner.ShouldBe(1);
  }
}
