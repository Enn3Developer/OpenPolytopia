namespace OpenPolytopia.Server;

using System.Net.Sockets;
using Common.Network;
using Common.Network.Packets;

public class GameServer {
  private readonly ServerConnection _server;

  public GameServer(int port) {
    _server = new ServerConnection(port);
    PacketRegistrar.RegisterAllPackets();
    _server.OnPacketReceived += ManagePacketAsync;
  }

  private async Task<bool> ManagePacketAsync(uint id, IPacket packet, NetworkStream stream, List<byte> bytes) {
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
}
