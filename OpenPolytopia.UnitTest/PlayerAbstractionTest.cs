namespace OpenPolytopia;

using Common;
using Common.Gameplay;
using Shouldly;
using Xunit;

public class PlayerAbstractionTest {
  private sealed record TestBot(TribeType Tribe, int Id) : IPlayer;

  [Fact]
  public async System.Threading.Tasks.Task AlternateParticipantCanGenerateAndPlay() {
    var pieces = GameTestFixture.BuildPieces();
    IPlayer[] players = [new Player(TribeType.Imperius, 1), new TestBot(TribeType.Imperius, 2)];
    var grid = new Grid(16);
    var cities = new CityManager(grid);
    await new TerrainGeneration(grid, cities, pieces.Tribes, players, 42).GenerateMapAsync();
    var troops = new TroopManager(16);
    troops.RegisterTroops(EmbeddedResources.LoadTroops()!);
    var game = new Common.Gameplay.Game(grid, cities, troops, pieces.Tribes, pieces.Buildings,
      pieces.TechTree, players);
    game.Start();
    game.EndTurn(1).Result.ShouldBe(GameActionResult.Ok);
    game.CurrentPlayer.ShouldBe(2);
    game.EndTurn(2).Result.ShouldBe(GameActionResult.Ok);
  }
}
