namespace OpenPolytopia.Server;

using OpenPolytopia.Common.Network;

internal static class Program {
  private static async Task Main(string[] args) {
    var port = NetworkConstants.DEFAULT_PORT;
    if (args.Length > 0 && !int.TryParse(args[0], out port)) {
      Console.Error.WriteLine($"Invalid port: {args[0]}");
      Environment.Exit(1);
    }

    using var gameServer = new GameServer(port);

    // stop gracefully on ctrl+c
    Console.CancelKeyPress += (_, eventArgs) => {
      eventArgs.Cancel = true;
      gameServer.Stop();
    };

    await gameServer.RunAsync();
  }
}
