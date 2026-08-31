namespace OpenPolytopia;

using Godot;
using Common;
using Shouldly;

public class ScoreTest {
  [Fact]
  public void TestPlayerScore() {
    var playerOneScore = new Score();
    playerOneScore.ScoreValue.ShouldBe(0);
    var playerTwoScore = new Score();
    playerTwoScore.ScoreValue.ShouldBe(0);
    playerOneScore.AddScore(ScoreType.ClaimedTile);
    playerOneScore.ScoreValue.ShouldBe(20);
  }

  [Fact]
  public void TestNegativeScore() {
    var playerOneScore = new Score();
    playerOneScore.AddScore(ScoreType.LoseCity(8));
    playerOneScore.ScoreValue.ShouldBe(0);
  }

  [Fact]
  public void TestLoseCity() {
    var playerOneScore = new Score();
    playerOneScore.AddScore(ScoreType.VillageConquered);
    playerOneScore.ScoreValue.ShouldBe(100);
    playerOneScore.AddScore(ScoreType.CityLevelUp(3));
    playerOneScore.ScoreValue.ShouldBe(250);
    playerOneScore.AddScore(ScoreType.LoseCity(3));
    playerOneScore.ScoreValue.ShouldBe(0);
  }

  [Fact]
  public void TestTroopSpawned() {
    var playerOneScore = new Score();
    playerOneScore.AddScore(ScoreType.TroopSpawned(3, 2));
    playerOneScore.ScoreValue.ShouldBe(45);
  }

  [Fact]
  public void TestLoseTroop() {
    var playerOneScore = new Score();
    playerOneScore.AddScore(ScoreType.TroopSpawned(2, 2));
    playerOneScore.ScoreValue.ShouldBe(30);
    playerOneScore.AddScore(ScoreType.LoseTroop(2));
    playerOneScore.ScoreValue.ShouldBe(15);
  }

  [Fact]
  public void TestDestroyedUndoesBuilt() {
    var playerOneScore = new Score();
    playerOneScore.AddScore(ScoreType.MonumentsBuilt);
    playerOneScore.AddScore(ScoreType.ParkBuilt);
    playerOneScore.AddScore(ScoreType.TemplesBuilt);
    playerOneScore.ScoreValue.ShouldBe(700);
    playerOneScore.AddScore(ScoreType.MonumentsDestroyed);
    playerOneScore.AddScore(ScoreType.ParkDestroyed);
    playerOneScore.AddScore(ScoreType.TemplesDestroyed);
    playerOneScore.ScoreValue.ShouldBe(0);
  }
}
