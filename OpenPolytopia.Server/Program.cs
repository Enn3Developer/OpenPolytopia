namespace OpenPolytopia.Server;

using OpenPolytopia.Common.Network;

internal static class Program {
  /// <summary>
  /// Environment variable that overrides the default port
  /// </summary>
  private const string PORT_ENV = "OPENPOLYTOPIA_PORT";

  /// <summary>
  /// Environment variable that overrides the default bind address
  /// </summary>
  private const string BIND_ADDRESS_ENV = "OPENPOLYTOPIA_BIND_ADDRESS";

  /// <summary>
  /// Entry point of the server
  /// </summary>
  /// <remarks>
  /// Usage: <c>OpenPolytopia.Server [port] [bind-address]</c>;
  /// both arguments are optional and fall back to the
  /// <c>OPENPOLYTOPIA_PORT</c>/<c>OPENPOLYTOPIA_BIND_ADDRESS</c> environment variables,
  /// then to port <see cref="NetworkConstants.DEFAULT_PORT"/> on every interface
  /// </remarks>
  private static async Task Main(string[] args) {
    var portValue = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable(PORT_ENV);
    var port = NetworkConstants.DEFAULT_PORT;
    if (!string.IsNullOrWhiteSpace(portValue) && !int.TryParse(portValue, out port)) {
      Console.Error.WriteLine($"Invalid port: {portValue}");
      Environment.Exit(1);
    }

    var bindAddress = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable(BIND_ADDRESS_ENV);
    if (string.IsNullOrWhiteSpace(bindAddress)) {
      bindAddress = null;
    }

    using var gameServer = new GameServer(port, bindAddress);

    // stop gracefully on ctrl+c
    Console.CancelKeyPress += (_, eventArgs) => {
      eventArgs.Cancel = true;
      gameServer.Stop();
    };

    await gameServer.RunAsync();
  }
}
