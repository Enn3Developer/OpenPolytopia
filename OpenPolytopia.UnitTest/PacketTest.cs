namespace OpenPolytopia;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Common;
using Common.Gameplay;
using Common.Network;
using Common.Network.Packets;
using Godot;
using Shouldly;

public class PacketTest {
  private static T RoundTrip<T>(T packet) where T : IPacket, new() {
    List<byte> bytes = [];
    packet.Serialize(bytes);
    var deserialized = new T();
    var index = 0u;
    deserialized.Deserialize(bytes.ToArray(), ref index);
    index.ShouldBe((uint)bytes.Count);
    return deserialized;
  }

  private static async Task<IPacket?> ReadBackAsync(byte[] bytes) {
    using var stream = new MemoryStream(bytes);
    return await PacketProtocol.ReadPacketAsync(stream);
  }

  [Fact]
  public void TestHandshake() {
    var packet = RoundTrip(new HandshakePacket { Version = "0.1.0" });
    packet.Version.ShouldBe("0.1.0");
  }

  [Fact]
  public void TestHandshakeResponse() {
    var packet = RoundTrip(new HandshakeResponsePacket { Ok = true, PlayerId = 42 });
    packet.Ok.ShouldBeTrue();
    packet.PlayerId.ShouldBe(42u);
  }

  [Fact]
  public void TestKeepAlive() {
    var packet = RoundTrip(new KeepAlivePacket());
    packet.ShouldNotBeNull();
  }

  [Fact]
  public void TestSetName() {
    var packet = RoundTrip(new SetNamePacket { Name = "Tester àèù" });
    packet.Name.ShouldBe("Tester àèù");
  }

  [Fact]
  public void TestSetNameResponse() {
    var packet = RoundTrip(new SetNameResponsePacket { Ok = true });
    packet.Ok.ShouldBeTrue();
  }

  [Fact]
  public void TestGetLobbies() {
    var packet = RoundTrip(new GetLobbiesPacket());
    packet.ShouldNotBeNull();
  }

  [Fact]
  public void TestGetLobbiesResponse() {
    var lobby = new LobbyData { Id = 123, MaxPlayers = 4, WorldSize = 16 };
    lobby.Players.Add(new LobbyPlayerData { PlayerId = 7, Name = "Test", Tribe = 2, Ready = true });
    var packet = RoundTrip(new GetLobbiesResponsePacket { Lobbies = [lobby] });
    packet.Lobbies.Count.ShouldBe(1);
    packet.Lobbies[0].Id.ShouldBe(123u);
    packet.Lobbies[0].MaxPlayers.ShouldBe(4u);
    packet.Lobbies[0].WorldSize.ShouldBe(16u);
    packet.Lobbies[0].Players.Count.ShouldBe(1);
    packet.Lobbies[0].Players[0].PlayerId.ShouldBe(7u);
    packet.Lobbies[0].Players[0].Name.ShouldBe("Test");
    packet.Lobbies[0].Players[0].Tribe.ShouldBe(2u);
    packet.Lobbies[0].Players[0].Ready.ShouldBeTrue();
    packet.Lobbies[0].ReadyCount.ShouldBe(1u);
  }

  [Fact]
  public void TestCreateLobby() {
    var packet = RoundTrip(new CreateLobbyPacket { MaxPlayers = 8, WorldSize = 18, Tribe = 3 });
    packet.MaxPlayers.ShouldBe(8u);
    packet.WorldSize.ShouldBe(18u);
    packet.Tribe.ShouldBe(3u);
  }

  [Fact]
  public void TestCreateLobbyResponse() {
    var packet = RoundTrip(new CreateLobbyResponsePacket { Result = LobbyActionResult.Ok, LobbyId = 99 });
    packet.Result.ShouldBe(LobbyActionResult.Ok);
    packet.LobbyId.ShouldBe(99u);
  }

  [Fact]
  public void TestJoinLobby() {
    var packet = RoundTrip(new JoinLobbyPacket { LobbyId = 21, Tribe = 4 });
    packet.LobbyId.ShouldBe(21u);
    packet.Tribe.ShouldBe(4u);
  }

  [Fact]
  public void TestJoinLobbyResponse() {
    var packet = RoundTrip(new JoinLobbyResponsePacket { Result = LobbyActionResult.LobbyFull, LobbyId = 5 });
    packet.Result.ShouldBe(LobbyActionResult.LobbyFull);
    packet.LobbyId.ShouldBe(5u);
  }

  [Fact]
  public void TestLeaveLobby() {
    var packet = RoundTrip(new LeaveLobbyPacket { LobbyId = 33 });
    packet.LobbyId.ShouldBe(33u);
  }

  [Fact]
  public void TestLeaveLobbyResponse() {
    var packet = RoundTrip(new LeaveLobbyResponsePacket { Result = LobbyActionResult.NotInLobby, LobbyId = 33 });
    packet.Result.ShouldBe(LobbyActionResult.NotInLobby);
    packet.LobbyId.ShouldBe(33u);
  }

  [Fact]
  public void TestSetReady() {
    var packet = RoundTrip(new SetReadyPacket { LobbyId = 11, Ready = true });
    packet.LobbyId.ShouldBe(11u);
    packet.Ready.ShouldBeTrue();
  }

  [Fact]
  public void TestSetReadyResponse() {
    var packet = RoundTrip(new SetReadyResponsePacket { Result = LobbyActionResult.Ok, LobbyId = 11 });
    packet.Result.ShouldBe(LobbyActionResult.Ok);
    packet.LobbyId.ShouldBe(11u);
  }

  [Fact]
  public void TestLobbyUpdated() {
    var lobby = new LobbyData { Id = 55, MaxPlayers = 2, WorldSize = 11 };
    lobby.Players.Add(new LobbyPlayerData { PlayerId = 9, Name = "Test", Tribe = 1 });
    var packet = RoundTrip(new LobbyUpdatedPacket { Lobby = lobby });
    packet.Lobby.Id.ShouldBe(55u);
    packet.Lobby.MaxPlayers.ShouldBe(2u);
    packet.Lobby.WorldSize.ShouldBe(11u);
    packet.Lobby.Players.Count.ShouldBe(1);
    packet.Lobby.Players[0].Name.ShouldBe("Test");
  }

  [Fact]
  public void TestLobbyDeleted() {
    var packet = RoundTrip(new LobbyDeletedPacket { LobbyId = 55 });
    packet.LobbyId.ShouldBe(55u);
  }

  [Fact]
  public void TestGameStarted() {
    var packet = RoundTrip(new GameStartedPacket {
      LobbyId = 3, Players = [new LobbyPlayerData { PlayerId = 1, Name = "A" }, new LobbyPlayerData { PlayerId = 2, Name = "B" }]
    });
    packet.LobbyId.ShouldBe(3u);
    packet.Players.Count.ShouldBe(2);
    packet.Players[1].Name.ShouldBe("B");
  }

  [Fact]
  public void TestFraming() {
    PacketRegistrar.RegisterAllPackets();
    var packet = new HandshakePacket { Version = "0.1.0" };
    List<byte> bytes = [];
    PacketProtocol.FramePacket(packet, bytes);

    // [content length][packet id][payload]
    var index = 0u;
    var buffer = bytes.ToArray();
    var contentLength = UIntSerialization.Read(buffer, ref index);
    contentLength.ShouldBe((uint)bytes.Count - 4);
    var packetId = UIntSerialization.Read(buffer, ref index);
    packetId.ShouldBe(1u);
  }

  [Fact]
  public async Task TestReadPacket() {
    PacketRegistrar.RegisterAllPackets();
    var packet = await ReadBackAsync(PacketProtocol.FramePacket(new SetNamePacket { Name = "Tester" }));
    packet.ShouldBeOfType<SetNamePacket>().Name.ShouldBe("Tester");
  }

  [Fact]
  public async Task TestReadManyPackets() {
    PacketRegistrar.RegisterAllPackets();
    List<byte> bytes = [];
    PacketProtocol.FramePacket(new HandshakePacket { Version = "0.1.0" }, bytes);
    PacketProtocol.FramePacket(new SetReadyPacket { LobbyId = 7, Ready = true }, bytes);

    using var stream = new MemoryStream(bytes.ToArray());
    (await PacketProtocol.ReadPacketAsync(stream)).ShouldBeOfType<HandshakePacket>().Version.ShouldBe("0.1.0");
    var setReady = (await PacketProtocol.ReadPacketAsync(stream)).ShouldBeOfType<SetReadyPacket>();
    setReady.LobbyId.ShouldBe(7u);
    setReady.Ready.ShouldBeTrue();
  }

  [Fact]
  public async Task TestReadSkipsUnknownPacket() {
    PacketRegistrar.RegisterAllPackets();

    // [content length][unregistered packet id][payload], followed by a valid packet
    List<byte> bytes = [];
    8u.Serialize(bytes);
    0xDEADBEEFu.Serialize(bytes);
    0u.Serialize(bytes);
    PacketProtocol.FramePacket(new KeepAlivePacket(), bytes);

    using var stream = new MemoryStream(bytes.ToArray());
    (await PacketProtocol.ReadPacketAsync(stream)).ShouldBeNull();

    // the payload of the unknown packet must be consumed too
    (await PacketProtocol.ReadPacketAsync(stream)).ShouldBeOfType<KeepAlivePacket>();
  }

  [Fact]
  public async Task TestReadRejectsTinyPacket() {
    // a packet must contain at least the 4 bytes of the packet id
    List<byte> bytes = [];
    3u.Serialize(bytes);
    await Should.ThrowAsync<ProtocolViolationException>(() => ReadBackAsync([.. bytes]));
  }

  [Fact]
  public async Task TestReadRejectsOversizedPacket() {
    List<byte> bytes = [];
    (NetworkConstants.MAX_PACKET_SIZE + 1).Serialize(bytes);
    await Should.ThrowAsync<ProtocolViolationException>(() => ReadBackAsync([.. bytes]));
  }

  [Fact]
  public async Task TestReadRejectsMalformedPayload() {
    PacketRegistrar.RegisterAllPackets();

    // a SetNamePacket whose string claims more bytes than the packet contains
    List<byte> content = [];
    PacketRegistrar.GetPacketId(new SetNamePacket()).Serialize(content);
    100u.Serialize(content);

    List<byte> bytes = [];
    ((uint)content.Count).Serialize(bytes);
    bytes.AddRange(content);

    await Should.ThrowAsync<ProtocolViolationException>(() => ReadBackAsync([.. bytes]));
  }

  [Fact]
  public void TestIntSerializationNegative() {
    List<byte> bytes = [];
    (-12345).Serialize(bytes);
    var index = 0u;
    IntSerialization.Read(bytes.ToArray(), ref index).ShouldBe(-12345);
    index.ShouldBe((uint)bytes.Count);
  }

  [Fact]
  public void TestAdditionalIntegralSerializers() {
    List<byte> bytes = [];
    ((sbyte)-42).Serialize(bytes);
    ((short)-12345).Serialize(bytes);
    ((ushort)54321).Serialize(bytes);
    (-1234567890123456789L).Serialize(bytes);

    var index = 0u;
    SByteSerialization.Read(bytes.ToArray(), ref index).ShouldBe((sbyte)-42);
    ShortSerialization.Read(bytes.ToArray(), ref index).ShouldBe((short)-12345);
    UShortSerialization.Read(bytes.ToArray(), ref index).ShouldBe((ushort)54321);
    LongSerialization.Read(bytes.ToArray(), ref index).ShouldBe(-1234567890123456789L);
    index.ShouldBe((uint)bytes.Count);
  }

  [Fact]
  public void TestVector2ISerializationNegative() {
    List<byte> bytes = [];
    new Vector2I(-3, -7).Serialize(bytes);
    var index = 0u;
    Vector2ISerialization.Read(bytes.ToArray(), ref index).ShouldBe(new Vector2I(-3, -7));
    index.ShouldBe((uint)bytes.Count);
  }

  [Fact]
  public void TestArraySerializationRejectsOversizedLength() {
    // a length prefix bigger than the payload must fail before allocating, like every other malformed payload
    List<byte> bytes = [];
    uint.MaxValue.Serialize(bytes);
    var index = 0u;
    Should.Throw<ArgumentOutOfRangeException>(() => UIntArraySerialization.Read(bytes.ToArray(), ref index));
    index = 0;
    Should.Throw<ArgumentOutOfRangeException>(() => ULongArraySerialization.Read(bytes.ToArray(), ref index));
  }

  [Fact]
  public void TestUIntArraySerializationEmpty() {
    List<byte> bytes = [];
    Array.Empty<uint>().Serialize(bytes);
    var index = 0u;
    UIntArraySerialization.Read(bytes.ToArray(), ref index).ShouldBeEmpty();
    index.ShouldBe((uint)bytes.Count);
  }

  [Fact]
  public void TestULongArraySerializationEmpty() {
    List<byte> bytes = [];
    Array.Empty<ulong>().Serialize(bytes);
    var index = 0u;
    ULongArraySerialization.Read(bytes.ToArray(), ref index).ShouldBeEmpty();
    index.ShouldBe((uint)bytes.Count);
  }

  [Fact]
  public void TestStringListSerializationEmpty() {
    List<byte> bytes = [];
    new List<string>().Serialize(bytes);
    List<string> result = [];
    var index = 0u;
    result.Deserialize(bytes.ToArray(), ref index);
    result.ShouldBeEmpty();
    index.ShouldBe((uint)bytes.Count);
  }

  [Fact]
  public void TestGetGameState() {
    var packet = RoundTrip(new GetGameStatePacket { GameId = 7 });
    packet.GameId.ShouldBe(7ul);
  }

  [Fact]
  public void TestGameState() {
    var player = new GamePlayerData {
      PlayerId = 1, Name = "A", Tribe = 2, Stars = 5, Score = 10, Alive = true, ResearchedTechs = ["warrior"]
    };
    var packet = RoundTrip(new GameStatePacket {
      Result = GameActionResult.Ok, GameId = 3, WorldSize = 16, Turn = 2, CurrentPlayer = 1, MaxTurns = 30,
      Over = false, Winner = 0, Players = [player], Tiles = [1ul, 2ul], Troops = [0u, 5u]
    });
    packet.GameId.ShouldBe(3ul);
    packet.WorldSize.ShouldBe(16u);
    packet.Players[0].Name.ShouldBe("A");
    packet.Tiles.ShouldBe([1ul, 2ul]);
    packet.Troops.ShouldBe([0u, 5u]);
  }

  [Fact]
  public void TestMoveTroop() {
    var packet = RoundTrip(new MoveTroopPacket { GameId = 1, From = new Vector2I(1, 2), To = new Vector2I(3, 4) });
    packet.From.ShouldBe(new Vector2I(1, 2));
    packet.To.ShouldBe(new Vector2I(3, 4));
  }

  [Fact]
  public void TestMoveTroopResponse() {
    var packet = RoundTrip(new MoveTroopResponsePacket { Result = GameActionResult.Unreachable });
    packet.Result.ShouldBe(GameActionResult.Unreachable);
  }

  [Fact]
  public void TestTroopMoved() {
    var update = new GameUpdate { Tiles = [new TileUpdate { Index = 4, Tile = 9, Troop = 2 }] };
    var packet = RoundTrip(new TroopMovedPacket {
      GameId = 1, PlayerId = 2, From = new Vector2I(0, 0), To = new Vector2I(1, 1), Update = update
    });
    packet.PlayerId.ShouldBe(2u);
    packet.Update.Tiles[0].Index.ShouldBe(4u);
    packet.Update.Tiles[0].Tile.ShouldBe(9ul);
  }

  [Fact]
  public void TestAttack() {
    var packet = RoundTrip(new AttackPacket { GameId = 1, From = new Vector2I(1, 1), Target = new Vector2I(2, 2) });
    packet.From.ShouldBe(new Vector2I(1, 1));
    packet.Target.ShouldBe(new Vector2I(2, 2));
  }

  [Fact]
  public void TestAttackResponse() {
    var packet = RoundTrip(new AttackResponsePacket { Result = GameActionResult.OutOfRange });
    packet.Result.ShouldBe(GameActionResult.OutOfRange);
  }

  [Fact]
  public void TestCombat() {
    var packet = RoundTrip(new CombatPacket {
      GameId = 1, PlayerId = 1, From = new Vector2I(0, 0), Target = new Vector2I(1, 0), AttackerHp = 8,
      DefenderHp = 0, AttackerKilled = false, DefenderKilled = true, AttackerPosition = new Vector2I(1, 0),
      Update = new GameUpdate()
    });
    packet.AttackerHp.ShouldBe(8u);
    packet.DefenderKilled.ShouldBeTrue();
    packet.AttackerPosition.ShouldBe(new Vector2I(1, 0));
  }

  [Fact]
  public void TestTrainTroop() {
    var packet = RoundTrip(new TrainTroopPacket { GameId = 1, City = new Vector2I(2, 2), TroopType = 3 });
    packet.City.ShouldBe(new Vector2I(2, 2));
    packet.TroopType.ShouldBe(3u);
  }

  [Fact]
  public void TestTrainTroopResponse() {
    var packet = RoundTrip(new TrainTroopResponsePacket { Result = GameActionResult.CityFull });
    packet.Result.ShouldBe(GameActionResult.CityFull);
  }

  [Fact]
  public void TestTroopTrained() {
    var packet = RoundTrip(new TroopTrainedPacket {
      GameId = 1, PlayerId = 2, Position = new Vector2I(3, 3), TroopType = 1, Update = new GameUpdate()
    });
    packet.PlayerId.ShouldBe(2u);
    packet.Position.ShouldBe(new Vector2I(3, 3));
    packet.TroopType.ShouldBe(1u);
  }

  [Fact]
  public void TestResearchTech() {
    var packet = RoundTrip(new ResearchTechPacket { GameId = 1, TechId = "climbing" });
    packet.TechId.ShouldBe("climbing");
  }

  [Fact]
  public void TestResearchTechResponse() {
    var packet = RoundTrip(new ResearchTechResponsePacket { Result = GameActionResult.TechLocked });
    packet.Result.ShouldBe(GameActionResult.TechLocked);
  }

  [Fact]
  public void TestTechResearched() {
    var packet = RoundTrip(new TechResearchedPacket {
      GameId = 1, PlayerId = 2, TechId = "riding", Update = new GameUpdate()
    });
    packet.PlayerId.ShouldBe(2u);
    packet.TechId.ShouldBe("riding");
  }

  [Fact]
  public void TestBuild() {
    var packet = RoundTrip(new BuildPacket { GameId = 1, Position = new Vector2I(5, 5), Building = 2 });
    packet.Position.ShouldBe(new Vector2I(5, 5));
    packet.Building.ShouldBe(2u);
  }

  [Fact]
  public void TestBuildResponse() {
    var packet = RoundTrip(new BuildResponsePacket { Result = GameActionResult.MissingResource });
    packet.Result.ShouldBe(GameActionResult.MissingResource);
  }

  [Fact]
  public void TestBuildingBuilt() {
    var packet = RoundTrip(new BuildingBuiltPacket {
      GameId = 1, PlayerId = 2, Position = new Vector2I(5, 5), Building = 2, Update = new GameUpdate()
    });
    packet.PlayerId.ShouldBe(2u);
    packet.Position.ShouldBe(new Vector2I(5, 5));
    packet.Building.ShouldBe(2u);
  }

  [Fact]
  public void TestCapture() {
    var packet = RoundTrip(new CapturePacket { GameId = 1, Position = new Vector2I(6, 6) });
    packet.Position.ShouldBe(new Vector2I(6, 6));
  }

  [Fact]
  public void TestCaptureResponse() {
    var packet = RoundTrip(new CaptureResponsePacket { Result = GameActionResult.NotACaptureTarget });
    packet.Result.ShouldBe(GameActionResult.NotACaptureTarget);
  }

  [Fact]
  public void TestCityCaptured() {
    var packet = RoundTrip(new CityCapturedPacket {
      GameId = 1, PlayerId = 2, Position = new Vector2I(6, 6), PreviousOwner = 3, EliminatedPlayer = 3,
      Update = new GameUpdate()
    });
    packet.PreviousOwner.ShouldBe(3u);
    packet.EliminatedPlayer.ShouldBe(3u);
  }

  [Fact]
  public void TestEndTurn() {
    var packet = RoundTrip(new EndTurnPacket { GameId = 9 });
    packet.GameId.ShouldBe(9ul);
  }

  [Fact]
  public void TestEndTurnResponse() {
    var packet = RoundTrip(new EndTurnResponsePacket { Result = GameActionResult.NotYourTurn });
    packet.Result.ShouldBe(GameActionResult.NotYourTurn);
  }

  [Fact]
  public void TestTurnStarted() {
    var packet = RoundTrip(new TurnStartedPacket { GameId = 1, Turn = 4, PlayerId = 2, Update = new GameUpdate() });
    packet.Turn.ShouldBe(4u);
    packet.PlayerId.ShouldBe(2u);
  }

  [Fact]
  public void TestPlayerEliminated() {
    var packet = RoundTrip(new PlayerEliminatedPacket { GameId = 1, PlayerId = 3, Update = new GameUpdate() });
    packet.PlayerId.ShouldBe(3u);
  }

  [Fact]
  public void TestGameOver() {
    var player = new GamePlayerData { PlayerId = 1, Name = "A", Alive = true };
    var packet = RoundTrip(new GameOverPacket { GameId = 1, Winner = 1, Players = [player] });
    packet.Winner.ShouldBe(1u);
    packet.Players[0].Name.ShouldBe("A");
  }

  [Fact]
  public void TestGamePacketsAreRegistered() {
    PacketRegistrar.RegisterAllPackets();
    PacketRegistrar.CreatePacket(18).ShouldBeOfType<GetGameStatePacket>();
    PacketRegistrar.CreatePacket(19).ShouldBeOfType<GameStatePacket>();
    PacketRegistrar.CreatePacket(20).ShouldBeOfType<MoveTroopPacket>();
    PacketRegistrar.CreatePacket(21).ShouldBeOfType<MoveTroopResponsePacket>();
    PacketRegistrar.CreatePacket(22).ShouldBeOfType<TroopMovedPacket>();
    PacketRegistrar.CreatePacket(23).ShouldBeOfType<AttackPacket>();
    PacketRegistrar.CreatePacket(24).ShouldBeOfType<AttackResponsePacket>();
    PacketRegistrar.CreatePacket(25).ShouldBeOfType<CombatPacket>();
    PacketRegistrar.CreatePacket(26).ShouldBeOfType<TrainTroopPacket>();
    PacketRegistrar.CreatePacket(27).ShouldBeOfType<TrainTroopResponsePacket>();
    PacketRegistrar.CreatePacket(28).ShouldBeOfType<TroopTrainedPacket>();
    PacketRegistrar.CreatePacket(29).ShouldBeOfType<ResearchTechPacket>();
    PacketRegistrar.CreatePacket(30).ShouldBeOfType<ResearchTechResponsePacket>();
    PacketRegistrar.CreatePacket(31).ShouldBeOfType<TechResearchedPacket>();
    PacketRegistrar.CreatePacket(32).ShouldBeOfType<BuildPacket>();
    PacketRegistrar.CreatePacket(33).ShouldBeOfType<BuildResponsePacket>();
    PacketRegistrar.CreatePacket(34).ShouldBeOfType<BuildingBuiltPacket>();
    PacketRegistrar.CreatePacket(35).ShouldBeOfType<CapturePacket>();
    PacketRegistrar.CreatePacket(36).ShouldBeOfType<CaptureResponsePacket>();
    PacketRegistrar.CreatePacket(37).ShouldBeOfType<CityCapturedPacket>();
    PacketRegistrar.CreatePacket(38).ShouldBeOfType<EndTurnPacket>();
    PacketRegistrar.CreatePacket(39).ShouldBeOfType<EndTurnResponsePacket>();
    PacketRegistrar.CreatePacket(40).ShouldBeOfType<TurnStartedPacket>();
    PacketRegistrar.CreatePacket(41).ShouldBeOfType<PlayerEliminatedPacket>();
    PacketRegistrar.CreatePacket(42).ShouldBeOfType<GameOverPacket>();
  }
}
