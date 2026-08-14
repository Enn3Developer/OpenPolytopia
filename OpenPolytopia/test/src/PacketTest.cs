namespace OpenPolytopia;

using System.Collections.Generic;
using Chickensoft.GoDotTest;
using Common;
using Common.Network;
using Common.Network.Packets;
using Godot;
using Shouldly;

public class PacketTest(Node testScene) : TestClass(testScene) {
  private static T RoundTrip<T>(T packet) where T : IPacket, new() {
    List<byte> bytes = [];
    packet.Serialize(bytes);
    var deserialized = new T();
    var index = 0u;
    deserialized.Deserialize(bytes.ToArray(), ref index);
    index.ShouldBe((uint)bytes.Count);
    return deserialized;
  }

  [Test]
  public void TestHandshake() {
    var packet = RoundTrip(new HandshakePacket { Version = "0.1.0" });
    packet.Version.ShouldBe("0.1.0");
  }

  [Test]
  public void TestHandshakeResponse() {
    var packet = RoundTrip(new HandshakeResponsePacket { Ok = true, PlayerId = 42 });
    packet.Ok.ShouldBeTrue();
    packet.PlayerId.ShouldBe(42u);
  }

  [Test]
  public void TestKeepAlive() {
    var packet = RoundTrip(new KeepAlivePacket());
    packet.ShouldNotBeNull();
  }

  [Test]
  public void TestSetName() {
    var packet = RoundTrip(new SetNamePacket { Name = "Tester àèù" });
    packet.Name.ShouldBe("Tester àèù");
  }

  [Test]
  public void TestGetLobbiesResponse() {
    var lobby = new LobbyData { Id = 123, MaxPlayers = 4 };
    lobby.Players.Add(new LobbyPlayerData { PlayerId = 7, Name = "Test", Tribe = 2, Ready = true });
    var packet = RoundTrip(new GetLobbiesResponsePacket { Lobbies = [lobby] });
    packet.Lobbies.Count.ShouldBe(1);
    packet.Lobbies[0].Id.ShouldBe(123u);
    packet.Lobbies[0].MaxPlayers.ShouldBe(4u);
    packet.Lobbies[0].Players.Count.ShouldBe(1);
    packet.Lobbies[0].Players[0].PlayerId.ShouldBe(7u);
    packet.Lobbies[0].Players[0].Name.ShouldBe("Test");
    packet.Lobbies[0].Players[0].Tribe.ShouldBe(2u);
    packet.Lobbies[0].Players[0].Ready.ShouldBeTrue();
    packet.Lobbies[0].ReadyCount.ShouldBe(1u);
  }

  [Test]
  public void TestCreateLobby() {
    var packet = RoundTrip(new CreateLobbyPacket { MaxPlayers = 8, Tribe = 3 });
    packet.MaxPlayers.ShouldBe(8u);
    packet.Tribe.ShouldBe(3u);
  }

  [Test]
  public void TestCreateLobbyResponse() {
    var packet = RoundTrip(new CreateLobbyResponsePacket { Result = LobbyActionResult.Ok, LobbyId = 99 });
    packet.Result.ShouldBe(LobbyActionResult.Ok);
    packet.LobbyId.ShouldBe(99u);
  }

  [Test]
  public void TestJoinLobbyResponse() {
    var packet = RoundTrip(new JoinLobbyResponsePacket { Result = LobbyActionResult.LobbyFull, LobbyId = 5 });
    packet.Result.ShouldBe(LobbyActionResult.LobbyFull);
    packet.LobbyId.ShouldBe(5u);
  }

  [Test]
  public void TestSetReady() {
    var packet = RoundTrip(new SetReadyPacket { LobbyId = 11, Ready = true });
    packet.LobbyId.ShouldBe(11u);
    packet.Ready.ShouldBeTrue();
  }

  [Test]
  public void TestGameStarted() {
    var packet = RoundTrip(new GameStartedPacket {
      LobbyId = 3, Players = [new LobbyPlayerData { PlayerId = 1, Name = "A" }, new LobbyPlayerData { PlayerId = 2, Name = "B" }]
    });
    packet.LobbyId.ShouldBe(3u);
    packet.Players.Count.ShouldBe(2);
    packet.Players[1].Name.ShouldBe("B");
  }

  [Test]
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
}
