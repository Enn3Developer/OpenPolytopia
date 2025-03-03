namespace OpenPolytopia.Server;

internal static class Program {
  private static async Task Main(string[] args) {
    Console.WriteLine("Server starting");
    var gameServer = new GameServer(6969);
    await gameServer.Run();
  }
}
