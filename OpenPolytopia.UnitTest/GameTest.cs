namespace OpenPolytopia;

using System.Linq;
using Common;
using Common.Gameplay;
using Shouldly;

public class GameTest {
  [Fact]
  public void TestStartSpawnsWarriorOnEveryCapitalThatCanAct() {
    var game = GameTestFixture.NewStartedGame(2);

    foreach (var player in game.Players) {
      var cityId = game.CityIdsOf(player.Id).Single(id => game.Cities[id].Capital);
      var position = game.Grid.IndexToGridPosition(game.Cities.GetIndex(cityId));
      var troop = game.Troops[position];

      troop.IsValid().ShouldBeTrue();
      troop.Type.ShouldBe(TroopType.Warrior);
      troop.Player.ShouldBe((uint)player.Id);
      troop.Moved.ShouldBeFalse();
      troop.Attacked.ShouldBeFalse();
      game.Cities[cityId].Troops.ShouldBe(1);
    }
  }

  [Fact]
  public void TestIncomeAddedOnBeginTurn() {
    var game = GameTestFixture.NewGame();
    var player = game.Players[0];
    var startingStars = player.Stars;

    game.Start();

    var cityId = game.CityIdsOf(player.Id).Single();
    var income = game.Cities[cityId].Stars;

    game.Income(player.Id).ShouldBe(income);
    player.Stars.ShouldBe(startingStars + income);
  }

  [Fact]
  public void TestEndTurnWrongPlayer() {
    var game = GameTestFixture.NewStartedGame(2);
    var wrongPlayer = game.Players[1].Id;

    game.EndTurn(wrongPlayer).Result.ShouldBe(GameActionResult.NotYourTurn);
    game.CurrentPlayer.ShouldBe(game.Players[0].Id);
    game.Turn.ShouldBe(1u);
  }

  [Fact]
  public void TestTurnWrapsAndIncrements() {
    var game = GameTestFixture.NewStartedGame(3);
    var (p1, p2, p3) = (game.Players[0].Id, game.Players[1].Id, game.Players[2].Id);

    game.EndTurn(p1).NextPlayer.ShouldBe(p2);
    game.Turn.ShouldBe(1u);

    game.EndTurn(p2).NextPlayer.ShouldBe(p3);
    game.Turn.ShouldBe(1u);

    var result = game.EndTurn(p3);
    result.NextPlayer.ShouldBe(p1);
    game.Turn.ShouldBe(2u);
  }

  [Fact]
  public void TestHealingRespectsTerritoryOwnership() {
    var game = GameTestFixture.NewStartedGame(2);
    var (p1, p2) = (game.Players[0].Id, game.Players[1].Id);

    var capitalPosition = game.Grid.IndexToGridPosition(game.Cities.GetIndex(game.CityIdsOf(p1).Single()));
    var neutralPosition = GameTestFixture.P(4, 4);

    game.Troops.ModifyTroop(capitalPosition, (ref TroopData troop) => troop.Hp = 5);
    game.Troops.SpawnTroop(neutralPosition, (uint)p1, 0, TroopType.Warrior);
    game.Troops.ModifyTroop(neutralPosition, (ref TroopData troop) => troop.Hp = 5);

    // a full round trip: back to p1 on their next turn
    game.EndTurn(p1);
    game.EndTurn(p2);

    game.Troops[capitalPosition].Hp.ShouldBe(9u); // +4, on a tile p1 owns
    game.Troops[neutralPosition].Hp.ShouldBe(7u); // +2, on a tile nobody owns
  }

  [Fact]
  public void TestHealingCapsAtMaxHp() {
    var game = GameTestFixture.NewStartedGame(2);
    var (p1, p2) = (game.Players[0].Id, game.Players[1].Id);

    var capitalPosition = game.Grid.IndexToGridPosition(game.Cities.GetIndex(game.CityIdsOf(p1).Single()));
    game.Troops.ModifyTroop(capitalPosition, (ref TroopData troop) => troop.Hp = 9); // warrior max hp is 10

    game.EndTurn(p1);
    game.EndTurn(p2);

    game.Troops[capitalPosition].Hp.ShouldBe(10u);
  }

  [Fact]
  public void TestMovedTroopDoesNotHeal() {
    var game = GameTestFixture.NewStartedGame(2);
    var (p1, p2) = (game.Players[0].Id, game.Players[1].Id);

    var capitalPosition = game.Grid.IndexToGridPosition(game.Cities.GetIndex(game.CityIdsOf(p1).Single()));
    game.Troops.ModifyTroop(capitalPosition, (ref TroopData troop) => {
      troop.Hp = 5;
      troop.Moved = true;
    });

    game.EndTurn(p1);
    game.EndTurn(p2);

    game.Troops[capitalPosition].Hp.ShouldBe(5u);
    game.Troops[capitalPosition].Moved.ShouldBeFalse(); // actions still reset even without healing
  }

  [Fact]
  public void TestResignCurrentPlayerPassesTurn() {
    var game = GameTestFixture.NewStartedGame(3);
    var (p1, p2) = (game.Players[0].Id, game.Players[1].Id);

    var result = game.Resign(p1);

    result.Result.ShouldBe(GameActionResult.Ok);
    result.GameOver.ShouldBeFalse();
    result.NextPlayer.ShouldBe(p2);
    game.CurrentPlayer.ShouldBe(p2);
    game[p1].ShouldNotBeNull().Alive.ShouldBeFalse();
    game.Over.ShouldBeFalse();
  }

  [Fact]
  public void TestEliminationScoresLostTroops() {
    var game = GameTestFixture.NewStartedGame(2);
    var p1 = game.Players[0];
    var capitalId = game.CityIdsOf(p1.Id).Single();
    // the score can't go below 0, so give the player something to lose
    p1.Score.AddScore(ScoreType.ClaimedTile);

    game.Resign(p1.Id);

    // the starting warrior costs 2 stars, so losing it is worth -(2 * 5 + 5)
    p1.Score.ScoreValue.ShouldBe(ScoreType.ClaimedTile.ToInt() - ScoreType.TroopSpawned(1, 2).ToInt());
    game.Cities[capitalId].Troops.ShouldBe(0);
  }

  [Fact]
  public void TestLastPlayerStandingWins() {
    var game = GameTestFixture.NewStartedGame(3);
    var (p1, p2, p3) = (game.Players[0].Id, game.Players[1].Id, game.Players[2].Id);

    game.Resign(p1).GameOver.ShouldBeFalse();
    var result = game.Resign(p2);

    result.Result.ShouldBe(GameActionResult.Ok);
    result.GameOver.ShouldBeTrue();
    result.Winner.ShouldBe(p3);
    game.Over.ShouldBeTrue();
    game.Winner.ShouldBe(p3);
    game[p3].ShouldNotBeNull().Alive.ShouldBeTrue();
  }

  [Fact]
  public void TestMaxTurnsEndsGameWithHighestScoreWinning() {
    var game = GameTestFixture.NewStartedGame(2, new GameSettings { MaxTurns = 1 });
    var (p1, p2) = (game.Players[0].Id, game.Players[1].Id);

    // give p2 the higher score, so the winner isn't just decided by turn-order tie-break
    game[p2].ShouldNotBeNull().Score.AddScore(ScoreType.ClaimedTile);

    game.EndTurn(p1).GameOver.ShouldBeFalse();
    game.Turn.ShouldBe(1u);

    var result = game.EndTurn(p2);

    result.Result.ShouldBe(GameActionResult.Ok);
    result.GameOver.ShouldBeTrue();
    result.Winner.ShouldBe(p2);
    game.Over.ShouldBeTrue();
    game.Winner.ShouldBe(p2);
    game.CurrentPlayer.ShouldBe(p2); // stays put, the turn never actually began
  }
}
