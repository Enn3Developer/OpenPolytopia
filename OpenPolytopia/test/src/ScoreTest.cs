namespace OpenPolytopia;

using Godot;
using Chickensoft.GoDotTest;
using Shouldly;

public class ScoreTest(Node testScene) : TestClass(testScene) {
  [Test]
  public void TestPlayerScore() {
    var playerOneScore = new Score();
    playerOneScore.ScoreValue.ShouldBe(0);
    var playerTwoScore = new Score();
    playerTwoScore.ScoreValue.ShouldBe(0);
    playerOneScore.AddScore(ScoreType.ClaimedTile);
    playerOneScore.ScoreValue.ShouldBe(20);
  }

  [Test]
  public void TestNegativeScore() {
    var playerOneScore = new Score();
    playerOneScore.AddScore(ScoreType.LoseCity, 8);
    playerOneScore.ScoreValue.ShouldBe(0);
  }

  [Test]
  public void TestLoseCity() {
    var playerOneScore = new Score();
    playerOneScore.AddScore(ScoreType.VillageConquered);
    playerOneScore.ScoreValue.ShouldBe(100);
    playerOneScore.AddScore(ScoreType.CityLevelUp, 3);
    playerOneScore.ScoreValue.ShouldBe(250);
    playerOneScore.AddScore(ScoreType.LoseCity, 3);
    playerOneScore.ScoreValue.ShouldBe(0);
  }

}
