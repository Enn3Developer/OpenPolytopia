namespace OpenPolytopia.Server;

using System.Net.Sockets;
using Common.Network;
using Common.Network.Packets;

public class GameServer : IDisposable {
  private readonly ServerConnection _server;

  private readonly Dictionary<uint, string> _playerNames = new(32);

  public GameServer(int port) {
    _server = new ServerConnection(port);
    PacketRegistrar.RegisterAllPackets();
    _server.OnPacketReceived += ManagePacketAsync;
  }

  private async Task<bool> ManagePacketAsync(uint id, IPacket packet, NetworkStream stream, List<byte> bytes) {
    switch (packet) {
      case HandshakePacket handshakePacket:
        await stream.WritePacketAsync(new HandshakeResponsePacket { Ok = handshakePacket.Version == "0.1.0" }, bytes);
        break;
      case RegisterUserPacket registerUserPacket:
        _playerNames[id] = registerUserPacket.Name;
        await stream.WritePacketAsync(new RegisterUserResponsePacket { Ok = true }, bytes);
        break;
    }

    return false;
  }

  public async Task Run() {
    _server.Start();

    var run = true;
    while (run) {
      await _server.ListenAsync();
      _server.Update();
    }

    _server.Stop();
  }

  public void Dispose() {
    _server.Dispose();
    GC.SuppressFinalize(this);
  }
}
