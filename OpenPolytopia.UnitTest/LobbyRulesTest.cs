namespace OpenPolytopia;

using Common;
using Shouldly;

public class LobbyRulesTest {
  [Theory]
  [InlineData(11u, 9u)]
  [InlineData(12u, 9u)]
  [InlineData(13u, 9u)]
  [InlineData(14u, 16u)]
  [InlineData(16u, 16u)]
  [InlineData(18u, 16u)]
  [InlineData(20u, 16u)]
  [InlineData(30u, 16u)]
  public void TestMaxPlayersFor(uint worldSize, uint expected) =>
    LobbyRules.MaxPlayersFor(worldSize).ShouldBe(expected);

  [Fact]
  public void TestMaxPlayersNeverShrinks() {
    // a bigger world can always host at least as many players as a smaller one
    for (var worldSize = LobbyRules.MIN_WORLD_SIZE; worldSize < LobbyRules.MAX_WORLD_SIZE; worldSize++) {
      LobbyRules.MaxPlayersFor(worldSize + 1).ShouldBeGreaterThanOrEqualTo(LobbyRules.MaxPlayersFor(worldSize));
    }
  }

  [Fact]
  public void TestValidWorldSize() {
    LobbyRules.IsValidWorldSize(LobbyRules.MIN_WORLD_SIZE).ShouldBeTrue();
    LobbyRules.IsValidWorldSize(LobbyRules.MAX_WORLD_SIZE).ShouldBeTrue();
    LobbyRules.IsValidWorldSize(LobbyRules.MIN_WORLD_SIZE - 1).ShouldBeFalse();
    LobbyRules.IsValidWorldSize(LobbyRules.MAX_WORLD_SIZE + 1).ShouldBeFalse();
    LobbyRules.IsValidWorldSize(0).ShouldBeFalse();
  }

  [Fact]
  public void TestValidLobby() {
    LobbyRules.IsValidLobby(2, 11).ShouldBeTrue();
    LobbyRules.IsValidLobby(9, 11).ShouldBeTrue();
    LobbyRules.IsValidLobby(16, 14).ShouldBeTrue();
  }

  [Fact]
  public void TestSoloLobbyIsInvalid() {
    LobbyRules.IsValidLobby(0, 16).ShouldBeFalse();
    LobbyRules.IsValidLobby(1, 16).ShouldBeFalse();
    LobbyRules.IsValidLobby(LobbyRules.MIN_PLAYERS, 16).ShouldBeTrue();
  }

  [Fact]
  public void TestCrowdedSmallWorldIsInvalid() {
    // a world smaller than 14x14 can't host more than 9 players
    LobbyRules.IsValidLobby(10, 13).ShouldBeFalse();
    LobbyRules.IsValidLobby(10, 14).ShouldBeTrue();
    LobbyRules.IsValidLobby(17, 30).ShouldBeFalse();
  }

  [Fact]
  public void TestLobbyOnInvalidWorldIsInvalid() {
    LobbyRules.IsValidLobby(2, LobbyRules.MIN_WORLD_SIZE - 1).ShouldBeFalse();
    LobbyRules.IsValidLobby(2, LobbyRules.MAX_WORLD_SIZE + 1).ShouldBeFalse();
  }
}
