namespace OpenPolytopia.Server;

using Common.Network;

internal static class Program {
  private static async Task Main(string[] args) {
    Console.WriteLine("Server starting");
    using var server = new ServerConnection(6969);
    server.OnPacketReceived += (id, packet, stream, bytes) => {
      Console.WriteLine($"Client tried connecting: {id}; packet: {packet}");
      return false;
    };
    var run = true;

    PacketRegistrar.RegisterAllPackets();
    server.Start();

    while (run) {
      await server.ListenAsync();
      server.Update();
    }

    server.Stop();
  }
}
