namespace OpenPolytopia;

using Common;
using Common.Network.Packets;
using Server;
using Shouldly;

public class LobbyManagerTest {
  private const uint WORLD_SIZE = 16;

  private readonly LobbyManager _lobbyManager = new();

  private static LobbyPlayerData Player(uint playerId, string name = "Tester", uint tribe = 0) =>
    new() { PlayerId = playerId, Name = name, Tribe = tribe };

  /// <summary>
  /// Creates a lobby, failing the test if the parameters aren't valid
  /// </summary>
  private LobbyData NewLobby(uint maxPlayers, LobbyPlayerData creator, uint worldSize = WORLD_SIZE) {
    _lobbyManager.CreateLobby(maxPlayers, worldSize, creator, out var lobby).ShouldBe(LobbyActionResult.Ok);
    return lobby.ShouldNotBeNull();
  }

  /// <summary>
  /// Creates a full lobby whose players are all ready but the last one
  /// </summary>
  private LobbyData ReadyLobby(uint maxPlayers) {
    var lobby = NewLobby(maxPlayers, Player(1));
    for (var playerId = 2u; playerId <= maxPlayers; playerId++) {
      _lobbyManager.JoinLobby(lobby.Id, Player(playerId)).ShouldBe(LobbyActionResult.Ok);
    }

    for (var playerId = 1u; playerId < maxPlayers; playerId++) {
      _lobbyManager.SetReady(lobby.Id, playerId, true).ShouldBe(LobbyActionResult.Ok);
    }

    return lobby;
  }

  [Fact]
  public void TestCreateLobby() {
    var lobby = NewLobby(4, Player(1, "Creator"));

    lobby.MaxPlayers.ShouldBe(4u);
    lobby.WorldSize.ShouldBe(WORLD_SIZE);
    lobby.PlayersCount.ShouldBe(1u);
    lobby.Players[0].Name.ShouldBe("Creator");
    lobby.Starting.ShouldBeFalse();
    lobby.Started.ShouldBeFalse();

    _lobbyManager.LobbiesCount.ShouldBe(1);
    _lobbyManager[lobby.Id].ShouldBe(lobby);
  }

  [Fact]
  public void TestLobbyIdsAreUnique() {
    // ids start at 1, so 0 can be used as "no lobby"
    NewLobby(2, Player(1)).Id.ShouldBe(1u);
    NewLobby(2, Player(2)).Id.ShouldBe(2u);
    _lobbyManager.LobbiesCount.ShouldBe(2);
  }

  [Theory]
  [InlineData(1u, 16u)]
  [InlineData(17u, 16u)]
  [InlineData(10u, 13u)]
  [InlineData(2u, 10u)]
  [InlineData(2u, 31u)]
  public void TestCreateInvalidLobby(uint maxPlayers, uint worldSize) {
    _lobbyManager.CreateLobby(maxPlayers, worldSize, Player(1), out var lobby)
      .ShouldBe(LobbyActionResult.InvalidParameters);

    // an invalid lobby is never created, not even a half-built one
    lobby.ShouldBeNull();
    _lobbyManager.LobbiesCount.ShouldBe(0);
  }

  [Fact]
  public void TestInvalidLobbyDoesntConsumeId() {
    _lobbyManager.CreateLobby(99, WORLD_SIZE, Player(1), out _).ShouldBe(LobbyActionResult.InvalidParameters);

    NewLobby(2, Player(1)).Id.ShouldBe(1u);
  }

  [Fact]
  public void TestUnknownLobby() {
    _lobbyManager[42].ShouldBeNull();
    _lobbyManager.JoinLobby(42, Player(1)).ShouldBe(LobbyActionResult.LobbyNotFound);
    _lobbyManager.LeaveLobby(42, 1).ShouldBe(LobbyActionResult.LobbyNotFound);
    _lobbyManager.SetReady(42, 1, true).ShouldBe(LobbyActionResult.LobbyNotFound);
  }

  [Fact]
  public void TestJoinLobby() {
    var lobby = NewLobby(2, Player(1));

    _lobbyManager.JoinLobby(lobby.Id, Player(2, "Joiner")).ShouldBe(LobbyActionResult.Ok);
    lobby.PlayersCount.ShouldBe(2u);
    lobby[2].ShouldNotBeNull().Name.ShouldBe("Joiner");
  }

  [Fact]
  public void TestJoinTwice() {
    var lobby = NewLobby(4, Player(1));

    _lobbyManager.JoinLobby(lobby.Id, Player(1)).ShouldBe(LobbyActionResult.AlreadyJoinedLobby);
    lobby.PlayersCount.ShouldBe(1u);
  }

  [Fact]
  public void TestJoinFullLobby() {
    var lobby = NewLobby(2, Player(1));
    _lobbyManager.JoinLobby(lobby.Id, Player(2)).ShouldBe(LobbyActionResult.Ok);

    _lobbyManager.JoinLobby(lobby.Id, Player(3)).ShouldBe(LobbyActionResult.LobbyFull);
    lobby.PlayersCount.ShouldBe(2u);
  }

  [Fact]
  public void TestLeaveLobby() {
    var lobby = NewLobby(4, Player(1));
    _lobbyManager.JoinLobby(lobby.Id, Player(2));

    _lobbyManager.LeaveLobby(lobby.Id, 2).ShouldBe(LobbyActionResult.Ok);
    lobby.PlayersCount.ShouldBe(1u);
    lobby[2].ShouldBeNull();
    _lobbyManager[lobby.Id].ShouldNotBeNull();
  }

  [Fact]
  public void TestLeaveLobbyNotJoined() {
    var lobby = NewLobby(4, Player(1));

    _lobbyManager.LeaveLobby(lobby.Id, 2).ShouldBe(LobbyActionResult.NotInLobby);
    lobby.PlayersCount.ShouldBe(1u);
  }

  [Fact]
  public void TestLeaveLobbyRemovesEmptyLobby() {
    var lobby = NewLobby(4, Player(1));

    // the last player leaving takes the lobby with him
    _lobbyManager.LeaveLobby(lobby.Id, 1).ShouldBe(LobbyActionResult.Ok);
    _lobbyManager.LobbiesCount.ShouldBe(0);
    _lobbyManager[lobby.Id].ShouldBeNull();
  }

  [Fact]
  public void TestLeaveLobbyKeepsOtherLobbies() {
    var emptied = NewLobby(4, Player(1));
    var other = NewLobby(4, Player(2));

    _lobbyManager.LeaveLobby(emptied.Id, 1).ShouldBe(LobbyActionResult.Ok);

    _lobbyManager[emptied.Id].ShouldBeNull();
    _lobbyManager[other.Id].ShouldNotBeNull();
    _lobbyManager.LobbiesCount.ShouldBe(1);
  }

  [Fact]
  public void TestSetReady() {
    var lobby = NewLobby(4, Player(1));

    _lobbyManager.SetReady(lobby.Id, 1, true).ShouldBe(LobbyActionResult.Ok);
    lobby[1].ShouldNotBeNull().Ready.ShouldBeTrue();
    lobby.ReadyCount.ShouldBe(1u);

    _lobbyManager.SetReady(lobby.Id, 1, false).ShouldBe(LobbyActionResult.Ok);
    lobby[1].ShouldNotBeNull().Ready.ShouldBeFalse();
    lobby.ReadyCount.ShouldBe(0u);
  }

  [Fact]
  public void TestSetReadyNotJoined() {
    var lobby = NewLobby(4, Player(1));

    _lobbyManager.SetReady(lobby.Id, 2, true).ShouldBe(LobbyActionResult.NotInLobby);
    lobby.ReadyCount.ShouldBe(0u);
  }

  [Fact]
  public void TestLobbyStartsWhenFullAndReady() {
    var lobby = ReadyLobby(3);
    lobby.Starting.ShouldBeFalse();

    _lobbyManager.SetReady(lobby.Id, 3, true).ShouldBe(LobbyActionResult.Ok);
    lobby.Starting.ShouldBeTrue();
  }

  [Fact]
  public void TestLobbyDoesntStartWhenNotFull() {
    var lobby = NewLobby(3, Player(1));
    _lobbyManager.JoinLobby(lobby.Id, Player(2));

    _lobbyManager.SetReady(lobby.Id, 1, true);
    _lobbyManager.SetReady(lobby.Id, 2, true);

    // everyone is ready, but the lobby still has a free slot
    lobby.ReadyCount.ShouldBe(lobby.PlayersCount);
    lobby.Starting.ShouldBeFalse();
  }

  [Fact]
  public void TestJoiningDoesntStartLobby() {
    var lobby = NewLobby(2, Player(1));
    _lobbyManager.SetReady(lobby.Id, 1, true);

    // only SetReady can start a lobby, even if the joining player says he's ready
    var joiner = Player(2);
    joiner.Ready = true;
    _lobbyManager.JoinLobby(lobby.Id, joiner).ShouldBe(LobbyActionResult.Ok);

    lobby.PlayersCount.ShouldBe(lobby.MaxPlayers);
    lobby.ReadyCount.ShouldBe(lobby.PlayersCount);
    lobby.Starting.ShouldBeFalse();
  }

  [Fact]
  public void TestStartingLobbyIsLocked() {
    var lobby = ReadyLobby(2);
    _lobbyManager.SetReady(lobby.Id, 2, true);
    lobby.Starting.ShouldBeTrue();

    _lobbyManager.JoinLobby(lobby.Id, Player(3)).ShouldBe(LobbyActionResult.LobbyAlreadyStarted);
    _lobbyManager.LeaveLobby(lobby.Id, 1).ShouldBe(LobbyActionResult.LobbyAlreadyStarted);
    _lobbyManager.SetReady(lobby.Id, 1, false).ShouldBe(LobbyActionResult.LobbyAlreadyStarted);
    lobby.PlayersCount.ShouldBe(2u);
  }

  [Fact]
  public void TestIsPlayerInAnyLobby() {
    var lobby = NewLobby(4, Player(1));
    NewLobby(4, Player(2));

    _lobbyManager.IsPlayerInAnyLobby(1).ShouldBeTrue();
    _lobbyManager.IsPlayerInAnyLobby(2).ShouldBeTrue();
    _lobbyManager.IsPlayerInAnyLobby(3).ShouldBeFalse();

    _lobbyManager.LeaveLobby(lobby.Id, 1);
    _lobbyManager.IsPlayerInAnyLobby(1).ShouldBeFalse();
  }

  [Fact]
  public void TestRenamePlayerInLobbies() {
    var first = NewLobby(4, Player(1, "Old"));
    var second = NewLobby(4, Player(2));
    _lobbyManager.JoinLobby(second.Id, Player(1, "Old"));

    List<LobbyData> updated = [];
    _lobbyManager.RenamePlayerInLobbies(1, "New", updated);

    updated.Count.ShouldBe(2);
    first[1].ShouldNotBeNull().Name.ShouldBe("New");
    second[1].ShouldNotBeNull().Name.ShouldBe("New");
    second[2].ShouldNotBeNull().Name.ShouldBe("Tester");
  }

  [Fact]
  public void TestRenameUnknownPlayer() {
    NewLobby(4, Player(1));

    List<LobbyData> updated = [];
    _lobbyManager.RenamePlayerInLobbies(2, "New", updated);

    updated.ShouldBeEmpty();
  }

  [Fact]
  public void TestRemoveLobby() {
    var lobby = NewLobby(4, Player(1));

    _lobbyManager.RemoveLobby(lobby.Id);
    _lobbyManager.LobbiesCount.ShouldBe(0);
    _lobbyManager[lobby.Id].ShouldBeNull();

    // removing an unknown lobby is a no-op
    _lobbyManager.RemoveLobby(lobby.Id);
    _lobbyManager.LobbiesCount.ShouldBe(0);
  }

  [Fact]
  public void TestRemovePlayerFromAllLobbies() {
    var shared = NewLobby(4, Player(1));
    _lobbyManager.JoinLobby(shared.Id, Player(2));
    var owned = NewLobby(4, Player(3));
    var untouched = NewLobby(4, Player(4));

    List<LobbyData> updated = [];
    List<ulong> deleted = [];
    _lobbyManager.RemovePlayerFromAllLobbies(1, updated, deleted);
    _lobbyManager.RemovePlayerFromAllLobbies(3, updated, deleted);

    // the shared lobby survives with one player left, the one-player lobby is removed
    updated.ShouldBe([shared]);
    deleted.ShouldBe([owned.Id]);
    shared.PlayersCount.ShouldBe(1u);
    _lobbyManager[owned.Id].ShouldBeNull();
    _lobbyManager[untouched.Id].ShouldNotBeNull();
  }

  [Fact]
  public void TestRemovePlayerStopsStartingLobby() {
    var lobby = ReadyLobby(2);
    _lobbyManager.SetReady(lobby.Id, 2, true);
    lobby.Starting.ShouldBeTrue();

    List<LobbyData> updated = [];
    List<ulong> deleted = [];
    _lobbyManager.RemovePlayerFromAllLobbies(2, updated, deleted);

    // the lobby isn't full anymore, so its game must not start
    lobby.Starting.ShouldBeFalse();
    updated.ShouldBe([lobby]);
    deleted.ShouldBeEmpty();
  }

  [Fact]
  public void TestTakeStartingLobbies() {
    var starting = ReadyLobby(2);
    _lobbyManager.SetReady(starting.Id, 2, true);
    var waiting = NewLobby(2, Player(3));

    var taken = _lobbyManager.TakeStartingLobbies();

    taken.ShouldBe([starting]);
    starting.Started.ShouldBeTrue();

    // a started lobby leaves the manager, the waiting one stays
    _lobbyManager[starting.Id].ShouldBeNull();
    _lobbyManager.LobbiesCount.ShouldBe(1);
    _lobbyManager[waiting.Id].ShouldNotBeNull();
  }

  [Fact]
  public void TestTakeStartingLobbiesWithNoneStarting() {
    NewLobby(2, Player(1));

    _lobbyManager.TakeStartingLobbies().ShouldBeEmpty();
    _lobbyManager.LobbiesCount.ShouldBe(1);
  }

  [Fact]
  public void TestTakeStartingLobbiesIsIdempotent() {
    var lobby = ReadyLobby(2);
    _lobbyManager.SetReady(lobby.Id, 2, true);

    _lobbyManager.TakeStartingLobbies().Count.ShouldBe(1);

    // the lobby already left the manager, so it can't be started twice
    _lobbyManager.TakeStartingLobbies().ShouldBeEmpty();
  }
}
