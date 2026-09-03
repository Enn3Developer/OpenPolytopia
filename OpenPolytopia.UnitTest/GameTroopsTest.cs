namespace OpenPolytopia;

using System.Linq;
using Common;
using Common.Gameplay;
using Godot;
using Shouldly;

public class GameTroopsTest {
  [Fact]
  public void TestReachableTilesEmptyForNonOwner() {
    var game = GameTestFixture.NewStartedGame();
    var enemyCapital = game.Grid.IndexToGridPosition(game.Cities.GetIndex(game.CityIdsOf(2).First()));

    game.ReachableTiles(1, enemyCapital).ShouldBeEmpty();
  }

  [Fact]
  public void TestEscapeAllowsMovingAfterAttacking() {
    var game = GameTestFixture.NewStartedGame();
    var riderPos = GameTestFixture.P(2, 2);
    var enemyPos = GameTestFixture.P(3, 2);
    game.Troops.SpawnTroop(riderPos, 1, 0, TroopType.Rider); // has Escape
    game.Troops.SpawnTroop(enemyPos, 2, 0, TroopType.Warrior);

    game.Attack(1, riderPos, enemyPos).Result.ShouldBe(GameActionResult.Ok);
    game.MoveTroop(1, riderPos, riderPos + new Vector2I(-1, 0)).ShouldBe(GameActionResult.Ok);
  }

  [Fact]
  public void TestNoEscapeCannotMoveAfterAttacking() {
    var game = GameTestFixture.NewStartedGame();
    var warriorPos = GameTestFixture.P(2, 2);
    var enemyPos = GameTestFixture.P(3, 2);
    game.Troops.SpawnTroop(warriorPos, 1, 0, TroopType.Warrior); // no Escape
    game.Troops.SpawnTroop(enemyPos, 2, 0, TroopType.Warrior);

    game.Attack(1, warriorPos, enemyPos).Result.ShouldBe(GameActionResult.Ok);
    game.MoveTroop(1, warriorPos, warriorPos + new Vector2I(-1, 0)).ShouldBe(GameActionResult.TroopAlreadyAttacked);
  }

  [Fact]
  public void TestWarriorVsWarriorDamageMatchesFormula() {
    var game = GameTestFixture.NewStartedGame();
    var attackerPos = GameTestFixture.P(2, 2);
    var defenderPos = GameTestFixture.P(3, 2);
    game.Troops.SpawnTroop(attackerPos, 1, 0, TroopType.Warrior);
    game.Troops.SpawnTroop(defenderPos, 2, 0, TroopType.Warrior);

    var result = game.Attack(1, attackerPos, defenderPos);

    // two full hp warriors: DamageCalculator gives 5 damage each way (see DamageCalculatorTest.TestEvenMatch)
    result.Result.ShouldBe(GameActionResult.Ok);
    result.DefenderHp.ShouldBe(5u);
    result.AttackerHp.ShouldBe(5u);
  }

  [Fact]
  public void TestRangedAttackAtMaxRangeTakesNoRetaliation() {
    var game = GameTestFixture.NewStartedGame();
    var archerPos = GameTestFixture.P(2, 2);
    var warriorPos = GameTestFixture.P(4, 2); // distance 2, out of the warrior's range 1

    game.Troops.SpawnTroop(archerPos, 1, 0, TroopType.Archer);
    game.Troops.SpawnTroop(warriorPos, 2, 0, TroopType.Warrior);

    var result = game.Attack(1, archerPos, warriorPos);

    result.Result.ShouldBe(GameActionResult.Ok);
    result.AttackerHp.ShouldBe(10u);
  }

  [Fact]
  public void TestMeleeRangeRetaliationAtDistance1() {
    var game = GameTestFixture.NewStartedGame();
    var archerPos = GameTestFixture.P(2, 2);
    var warriorPos = GameTestFixture.P(3, 2); // distance 1, within the warrior's range 1

    game.Troops.SpawnTroop(archerPos, 1, 0, TroopType.Archer);
    game.Troops.SpawnTroop(warriorPos, 2, 0, TroopType.Warrior);

    var result = game.Attack(1, archerPos, warriorPos);

    result.Result.ShouldBe(GameActionResult.Ok);
    result.AttackerHp.ShouldBeLessThan(10u);
  }

  [Fact]
  public void TestMeleeKillMovesAttackerIn() {
    var game = GameTestFixture.NewStartedGame();
    var attackerPos = GameTestFixture.P(2, 2);
    var defenderPos = GameTestFixture.P(3, 2);
    game.Troops.SpawnTroop(attackerPos, 1, 0, TroopType.Warrior);
    game.Troops.SpawnTroop(defenderPos, 2, 0, TroopType.Warrior);
    game.Troops.ModifyTroop(defenderPos, (ref TroopData data) => data.Hp = 1);

    var result = game.Attack(1, attackerPos, defenderPos);

    result.DefenderKilled.ShouldBeTrue();
    result.AttackerPosition.ShouldBe(defenderPos);
    game.Troops[attackerPos].IsValid().ShouldBeFalse();
    game.Troops[defenderPos].IsValid().ShouldBeTrue();
    game.Troops[defenderPos].Moved.ShouldBeFalse(); // moving into the vacated tile isn't a move
  }

  [Fact]
  public void TestRangedKillDoesNotMoveAttackerIn() {
    var game = GameTestFixture.NewStartedGame();
    var attackerPos = GameTestFixture.P(2, 2);
    var defenderPos = GameTestFixture.P(4, 2); // distance 2, within the archer's range 2
    game.Troops.SpawnTroop(attackerPos, 1, 0, TroopType.Archer);
    game.Troops.SpawnTroop(defenderPos, 2, 0, TroopType.Warrior);
    game.Troops.ModifyTroop(defenderPos, (ref TroopData data) => data.Hp = 1);

    var result = game.Attack(1, attackerPos, defenderPos);

    result.DefenderKilled.ShouldBeTrue();
    result.AttackerPosition.ShouldBe(attackerPos);
    game.Troops[attackerPos].IsValid().ShouldBeTrue();
    game.Troops[defenderPos].IsValid().ShouldBeFalse();
  }

  [Fact]
  public void TestZeroDefenseCatapultTakesNoRetaliationDamage() {
    var game = GameTestFixture.NewStartedGame();
    var attackerPos = GameTestFixture.P(2, 2);
    var catapultPos = GameTestFixture.P(3, 2); // distance 1, within the catapult's range 3

    game.Troops.SpawnTroop(attackerPos, 1, 0, TroopType.Warrior);
    game.Troops.SpawnTroop(catapultPos, 2, 0, TroopType.Catapult);

    var result = game.Attack(1, attackerPos, catapultPos);

    // catapult's defense is 0, so the retaliation formula degenerates to 0 damage, not a crash
    result.Result.ShouldBe(GameActionResult.Ok);
    result.DefenderKilled.ShouldBeFalse();
    result.AttackerHp.ShouldBe(10u);
  }

  [Fact]
  public void TestDashAllowsAttackingAfterMoving() {
    var game = GameTestFixture.NewStartedGame();
    var attackerPos = GameTestFixture.P(2, 2);
    var defenderPos = GameTestFixture.P(3, 2);
    game.Troops.SpawnTroop(attackerPos, 1, 0, TroopType.Warrior); // warrior has Dash
    game.Troops.SpawnTroop(defenderPos, 2, 0, TroopType.Warrior);
    game.Troops.ModifyTroop(attackerPos, (ref TroopData data) => data.Moved = true);

    game.Attack(1, attackerPos, defenderPos).Result.ShouldBe(GameActionResult.Ok);
  }

  [Fact]
  public void TestNoDashCannotAttackAfterMoving() {
    var game = GameTestFixture.NewStartedGame();
    var attackerPos = GameTestFixture.P(2, 2);
    var defenderPos = GameTestFixture.P(3, 2);
    game.Troops.SpawnTroop(attackerPos, 1, 0, TroopType.Defender); // no Dash
    game.Troops.SpawnTroop(defenderPos, 2, 0, TroopType.Warrior);
    game.Troops.ModifyTroop(attackerPos, (ref TroopData data) => data.Moved = true);

    game.Attack(1, attackerPos, defenderPos).Result.ShouldBe(GameActionResult.TroopAlreadyMoved);
  }

  [Fact]
  public void TestFortifyAndWallReduceDamageTaken() {
    // baseline: warrior vs warrior in the open, no bonus
    var openGame = GameTestFixture.NewStartedGame();
    var openAttacker = GameTestFixture.P(2, 2);
    var openDefender = GameTestFixture.P(3, 2);
    openGame.Troops.SpawnTroop(openAttacker, 1, 0, TroopType.Warrior);
    openGame.Troops.SpawnTroop(openDefender, 2, 0, TroopType.Warrior);
    var openResult = openGame.Attack(1, openAttacker, openDefender);

    // the capital already holds player 2's starting warrior, fortified on its own city (1.5x)
    var fortGame = GameTestFixture.NewStartedGame();
    var fortCapitalId = fortGame.CityIdsOf(2).First();
    var fortCapitalPos = fortGame.Grid.IndexToGridPosition(fortGame.Cities.GetIndex(fortCapitalId));
    var fortAttacker = fortCapitalPos + new Vector2I(1, 0);
    fortGame.Troops.SpawnTroop(fortAttacker, 1, 0, TroopType.Warrior);
    var fortResult = fortGame.Attack(1, fortAttacker, fortCapitalPos);

    fortResult.DefenderHp.ShouldBeGreaterThan(openResult.DefenderHp);

    // same setup, but the city also has a wall (4.0x)
    var wallGame = GameTestFixture.NewStartedGame();
    var wallCapitalId = wallGame.CityIdsOf(2).First();
    var wallCapitalPos = wallGame.Grid.IndexToGridPosition(wallGame.Cities.GetIndex(wallCapitalId));
    wallGame.Cities.ModifyCity(wallCapitalId, (ref CityData city) => city.Wall = true);
    var wallAttacker = wallCapitalPos + new Vector2I(1, 0);
    wallGame.Troops.SpawnTroop(wallAttacker, 1, 0, TroopType.Warrior);
    var wallResult = wallGame.Attack(1, wallAttacker, wallCapitalPos);

    wallResult.DefenderHp.ShouldBeGreaterThan(fortResult.DefenderHp);
  }

  [Fact]
  public void TestPersistAllowsAttackingAgainAfterKill() {
    var game = GameTestFixture.NewStartedGame();
    var knightPos = GameTestFixture.P(2, 2);
    var firstTargetPos = GameTestFixture.P(3, 2); // distance 1 from the knight
    var secondTargetPos = GameTestFixture.P(3, 3); // distance 1 from the knight's post-kill position

    game.Troops.SpawnTroop(knightPos, 1, 0, TroopType.Knight);
    game.Troops.SpawnTroop(firstTargetPos, 2, 0, TroopType.Warrior);
    game.Troops.ModifyTroop(firstTargetPos, (ref TroopData data) => data.Hp = 1);
    game.Troops.SpawnTroop(secondTargetPos, 2, 0, TroopType.Warrior);
    game.Troops.ModifyTroop(secondTargetPos, (ref TroopData data) => data.Hp = 1);

    var firstResult = game.Attack(1, knightPos, firstTargetPos);
    firstResult.DefenderKilled.ShouldBeTrue();
    firstResult.AttackerPosition.ShouldBe(firstTargetPos);

    var secondResult = game.Attack(1, firstResult.AttackerPosition, secondTargetPos);
    secondResult.Result.ShouldBe(GameActionResult.Ok);
    secondResult.DefenderKilled.ShouldBeTrue();
  }

  [Fact]
  public void TestKilledTroopDecrementsCityTroopsAndScoresLoss() {
    var game = GameTestFixture.NewStartedGame();
    var defenderCityId = game.CityIdsOf(2).First();
    var defenderPos = game.Grid.IndexToGridPosition(game.Cities.GetIndex(defenderCityId));
    var attackerPos = defenderPos + new Vector2I(1, 0);

    game.Troops.ModifyTroop(defenderPos, (ref TroopData data) => data.Hp = 1);
    game.Cities[defenderCityId].Troops.ShouldBe(1); // the starting warrior
    game.Troops.SpawnTroop(attackerPos, 1, 0, TroopType.Warrior);

    // give the loser a positive score first: Score.ScoreValue never goes negative, LoseTroop alone would be invisible
    game[2]!.Score.AddScore(ScoreType.VillageConquered);
    var before = game[2]!.Score.ScoreValue;

    var result = game.Attack(1, attackerPos, defenderPos);

    result.DefenderKilled.ShouldBeTrue();
    game.Cities[defenderCityId].Troops.ShouldBe(0);
    game[2]!.Score.ScoreValue.ShouldBeLessThan(before);
  }

  [Fact]
  public void TestTrainTroopCapsAtCityLevelPlusOne() {
    var game = GameTestFixture.NewStartedGame();
    var capitalId = game.CityIdsOf(1).First();
    var capitalPos = game.Grid.IndexToGridPosition(game.Cities.GetIndex(capitalId));

    game.MoveTroop(1, capitalPos, capitalPos + new Vector2I(1, 0)).ShouldBe(GameActionResult.Ok);
    // the starting warrior already counts as 1 troop of this city; level 1 -> cap is 2
    game.TrainTroop(1, capitalPos, TroopType.Warrior).ShouldBe(GameActionResult.Ok);

    game.EndTurn(1);
    game.EndTurn(2);

    // vacate the tile again so the next training attempt fails on the cap, not on TileOccupied
    game.MoveTroop(1, capitalPos, capitalPos + new Vector2I(0, 1)).ShouldBe(GameActionResult.Ok);
    game.TrainTroop(1, capitalPos, TroopType.Warrior).ShouldBe(GameActionResult.CityFull);
  }

  [Fact]
  public void TestTrainTroopNotEnoughStars() {
    var game = GameTestFixture.NewStartedGame();
    var capitalId = game.CityIdsOf(1).First();
    var capitalPos = game.Grid.IndexToGridPosition(game.Cities.GetIndex(capitalId));
    game.MoveTroop(1, capitalPos, capitalPos + new Vector2I(1, 0));
    game.Players[0].Stars = 0;

    game.TrainTroop(1, capitalPos, TroopType.Warrior).ShouldBe(GameActionResult.NotEnoughStars);
  }

  [Fact]
  public void TestTrainTroopRequiresTech() {
    var game = GameTestFixture.NewStartedGame();
    var capitalId = game.CityIdsOf(1).First();
    var capitalPos = game.Grid.IndexToGridPosition(game.Cities.GetIndex(capitalId));
    game.MoveTroop(1, capitalPos, capitalPos + new Vector2I(1, 0));

    // Imperius starts with "organization" researched, not "archery"
    game.TrainTroop(1, capitalPos, TroopType.Archer).ShouldBe(GameActionResult.TechLocked);
  }

  [Fact]
  public void TestTrainedTroopCannotActThisTurn() {
    var game = GameTestFixture.NewStartedGame();
    var capitalId = game.CityIdsOf(1).First();
    var capitalPos = game.Grid.IndexToGridPosition(game.Cities.GetIndex(capitalId));
    game.MoveTroop(1, capitalPos, capitalPos + new Vector2I(1, 0));

    game.TrainTroop(1, capitalPos, TroopType.Warrior).ShouldBe(GameActionResult.Ok);

    var spawned = game.Troops[capitalPos];
    spawned.Moved.ShouldBeTrue();
    spawned.Attacked.ShouldBeTrue();
  }

  [Fact]
  public void TestTrainTroopScoresTroopSpawned() {
    var game = GameTestFixture.NewStartedGame();
    var capitalId = game.CityIdsOf(1).First();
    var capitalPos = game.Grid.IndexToGridPosition(game.Cities.GetIndex(capitalId));
    game.MoveTroop(1, capitalPos, capitalPos + new Vector2I(1, 0));
    var before = game.Players[0].Score.ScoreValue;

    game.TrainTroop(1, capitalPos, TroopType.Warrior).ShouldBe(GameActionResult.Ok);

    game.Players[0].Score.ScoreValue.ShouldBe(before + ScoreType.TroopSpawned(1, 2).ToInt());
  }
}
