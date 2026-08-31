namespace OpenPolytopia;

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Common;
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
}
