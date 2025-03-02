namespace OpenPolytopia;

using System.Collections.Generic;
using Chickensoft.GoDotTest;
using Common.Network.Packets;
using Godot;
using Shouldly;

public class PacketTest(Node testScene) : TestClass(testScene) {
  [Test]
  public void TestHandshake() {
    var packet = new HandshakePacket { Version = "0.1.0" };
    List<byte> bytes = [];
    packet.Serialize(bytes);
    var deserializedPacket = HandshakePacket.Default();
    deserializedPacket.Deserialize(bytes.ToArray());
    deserializedPacket.Version.ShouldBe("0.1.0");
  }

  [Test]
  public void TestHandshakeResponse() {
    var packet = new HandshakeResponsePacket { Ok = true };
    List<byte> bytes = [];
    packet.Serialize(bytes);
    var deserializedPacket = HandshakeResponsePacket.Default();
    deserializedPacket.Deserialize(bytes.ToArray());
    deserializedPacket.Ok.ShouldBeTrue();
  }

  [Test]
  public void TestKeepAlive() {
    var packet = new KeepAlivePacket { Captcha = 20u };
    List<byte> bytes = [];
    packet.Serialize(bytes);
    var deserializedPacket = KeepAlivePacket.Default();
    deserializedPacket.Deserialize(bytes.ToArray());
    deserializedPacket.Captcha.ShouldBe(20u);
  }

  [Test]
  public void TestGetLobbiesResponse() {
    var lobby = new Common.Lobby();
    lobby.AddPlayer(new Common.PlayerData { PlayerName = "Test" });
    var packet = new GetLobbiesResponsePacket { Lobbies = [lobby] };
    List<byte> bytes = [];
    packet.Serialize(bytes);
    var deserializedPacket = GetLobbiesResponsePacket.Default();
    deserializedPacket.Deserialize(bytes.ToArray());
    deserializedPacket.Lobbies.Count.ShouldBe(1);
    deserializedPacket.Lobbies[0].GetPlayers().Count.ShouldBe(1);
    deserializedPacket.Lobbies[0].GetPlayers()[0].PlayerName.ShouldBe("Test");
  }
}
