namespace OpenPolytopia;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Common.Network;
using Common.Network.Packets;
using Server;
using Shouldly;

/// <summary>
/// A <see cref="GameServer"/> listening on a free loopback port, backed by a throwaway SQLite file
/// </summary>
/// <remarks>
/// The database lives in a temporary directory owned by the test, so a restart can point a brand new server at the
/// very same file; disposing the harness stops the server and awaits its accept loop before disposing it, which is
/// what keeps the background loops from touching an already disposed store
/// </remarks>
internal sealed class TestServer : IAsyncDisposable {
  private readonly GameServer _server;
  private readonly Task _run;
  private bool _stopped;

  /// <summary>The loopback port the server is listening on</summary>
  public int Port { get; }

  /// <summary>Path of the SQLite database backing this server</summary>
  public string DatabasePath { get; }

  private TestServer(string databasePath, int port) {
    DatabasePath = databasePath;
    Port = port;
    _server = new GameServer(port, "127.0.0.1", databasePath);
    _run = _server.RunAsync();
  }

  /// <summary>
  /// Starts a server and waits until its listener actually accepts connections
  /// </summary>
  /// <param name="databasePath">the SQLite file to use; an existing one gets restored</param>
  public static async Task<TestServer> StartAsync(string databasePath) {
    PacketRegistrar.RegisterAllPackets();
    var server = new TestServer(databasePath, FreePort());
    await server.WaitUntilListeningAsync();
    return server;
  }

  /// <summary>
  /// Stops the server and awaits its loops, without disposing it
  /// </summary>
  public async Task StopAsync() {
    if (_stopped) {
      return;
    }

    _stopped = true;
    _server.Stop();
    // RunAsync returns once the accept loop observes the cancellation and closes every client
    await _run.WaitAsync(TimeSpan.FromSeconds(15));
  }

  public async ValueTask DisposeAsync() {
    await StopAsync();
    _server.Dispose();
  }

  /// <summary>
  /// Polls the port until a plain TCP connect succeeds
  /// </summary>
  private async Task WaitUntilListeningAsync() {
    await TestPolling.UntilAsync(async () => {
      try {
        using var probe = new TcpClient();
        await probe.ConnectAsync(IPAddress.Loopback, Port);
        return true;
      }
      catch (SocketException) {
        return false;
      }
    }, TimeSpan.FromSeconds(10), "the server never started listening");
  }

  /// <summary>
  /// Asks the OS for a free port, then hands it over to the server
  /// </summary>
  /// <remarks>
  /// Racy in theory, but the listener is only closed for the instant it takes the server to bind it, and every test
  /// asks for its own port
  /// </remarks>
  private static int FreePort() {
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
  }
}

/// <summary>
/// A real TCP client of a <see cref="TestServer"/>, with the boilerplate every test needs
/// </summary>
/// <remarks>
/// Packets the tests don't ask for (lobby broadcasts, keep alive packets already answered by
/// <see cref="ClientConnection"/>...) are buffered instead of dropped, so a test can assert on them later and the
/// order they arrive in never makes a test flaky
/// </remarks>
internal sealed class TestClient : IDisposable {
  private static readonly TimeSpan DEFAULT_TIMEOUT = TimeSpan.FromSeconds(20);

  private readonly ClientConnection _connection;
  private readonly List<IPacket> _buffered = [];

  /// <summary>Account id assigned by the server, valid after a successful authentication</summary>
  public uint AccountId { get; private set; }

  /// <summary>Session token of the last successful authentication</summary>
  public string Token { get; private set; } = "";

  /// <summary>Display name of the authenticated account</summary>
  public string Name { get; private set; } = "";

  /// <summary>Whether the transport is still up</summary>
  public bool Connected => _connection.Connected;

  private TestClient(ClientConnection connection) => _connection = connection;

  /// <summary>
  /// Connects to a server and completes the protocol handshake
  /// </summary>
  public static async Task<TestClient> ConnectAsync(TestServer server) {
    PacketRegistrar.RegisterAllPackets();
    var connection = new ClientConnection("127.0.0.1", server.Port);
    await connection.ConnectAsync();

    var client = new TestClient(connection);
    await client.SendAsync(new HandshakePacket { Version = NetworkConstants.VERSION });
    var response = await client.ExpectAsync<HandshakeResponsePacket>();
    response.Ok.ShouldBeTrue();
    return client;
  }

  public Task SendAsync(IPacket packet) => _connection.SendPacketAsync(packet);

  /// <summary>
  /// Registers a new account and remembers its id and token
  /// </summary>
  public Task<AuthenticationPacket> RegisterAsync(string username, string password) =>
    AuthenticateAsync(new RegisterAccountPacket { Username = username, Password = password });

  /// <summary>
  /// Logs into an existing account and remembers its id and token
  /// </summary>
  public Task<AuthenticationPacket> LoginAsync(string username, string password) =>
    AuthenticateAsync(new LoginPacket { Username = username, Password = password });

  /// <summary>
  /// Resumes a session from a token handed out by a previous authentication
  /// </summary>
  public Task<AuthenticationPacket> ResumeAsync(string token) =>
    AuthenticateAsync(new ResumeSessionPacket { Token = token });

  private async Task<AuthenticationPacket> AuthenticateAsync(IPacket request) {
    await SendAsync(request);
    // pbkdf2 with 600k iterations isn't instant, and the server serializes every packet through its state lock
    var response = await ExpectAsync<AuthenticationPacket>(timeout: TimeSpan.FromSeconds(30));

    if (response.Ok) {
      AccountId = response.PlayerId;
      Token = response.Token;
      Name = response.Name;
    }

    return response;
  }

  /// <summary>
  /// Waits for the first packet of a type matching an optional predicate
  /// </summary>
  /// <remarks>
  /// Already buffered packets are considered first; the matched one is consumed, everything else stays available
  /// for the next call
  /// </remarks>
  /// <param name="predicate">an optional extra condition the packet has to satisfy</param>
  /// <param name="timeout">how long to wait before failing the test</param>
  public async Task<T> ExpectAsync<T>(Func<T, bool>? predicate = null, TimeSpan? timeout = null) where T : IPacket {
    var deadline = timeout ?? DEFAULT_TIMEOUT;
    var packet = await TryReceiveAsync(predicate, deadline);
    return packet ?? throw new Xunit.Sdk.XunitException($"no matching {typeof(T).Name} arrived within {deadline}");
  }

  /// <summary>
  /// Waits for a packet the test expects <em>not</em> to arrive
  /// </summary>
  /// <returns>the packet if one arrived within <paramref name="timeout"/>, null otherwise</returns>
  public Task<T?> TryReceiveAsync<T>(Func<T, bool>? predicate, TimeSpan timeout) where T : IPacket =>
    TestPolling.PollAsync(() => {
      Drain();

      for (var index = 0; index < _buffered.Count; index++) {
        if (_buffered[index] is T match && (predicate == null || predicate(match))) {
          _buffered.RemoveAt(index);
          return match;
        }
      }

      return default;
    }, timeout);

  /// <summary>
  /// Waits until the server drops this connection
  /// </summary>
  public Task ShouldBeDisconnectedAsync(TimeSpan? timeout = null) => TestPolling.UntilAsync(
    () => Task.FromResult(!Connected), timeout ?? DEFAULT_TIMEOUT, "the server never closed the connection");

  private void Drain() {
    while (_connection.IncomingPackets.TryDequeue(out var packet)) {
      _buffered.Add(packet);
    }
  }

  public void Dispose() => _connection.Dispose();
}

/// <summary>
/// Deterministic waiting: every test waits for a condition with a deadline instead of sleeping for a fixed time
/// </summary>
internal static class TestPolling {
  private static readonly TimeSpan INTERVAL = TimeSpan.FromMilliseconds(10);

  /// <summary>
  /// Polls until <paramref name="probe"/> returns a non-default value or the timeout expires
  /// </summary>
  public static async Task<T?> PollAsync<T>(Func<T?> probe, TimeSpan timeout) {
    var deadline = DateTime.UtcNow + timeout;

    while (true) {
      var result = probe();
      if (!EqualityComparer<T?>.Default.Equals(result, default)) {
        return result;
      }

      if (DateTime.UtcNow >= deadline) {
        return default;
      }

      await Task.Delay(INTERVAL);
    }
  }

  /// <summary>
  /// Polls until <paramref name="condition"/> holds, failing the test when the timeout expires
  /// </summary>
  public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string message) {
    var deadline = DateTime.UtcNow + timeout;

    while (true) {
      if (await condition()) {
        return;
      }

      if (DateTime.UtcNow >= deadline) {
        throw new Xunit.Sdk.XunitException($"{message} (waited {timeout})");
      }

      await Task.Delay(INTERVAL);
    }
  }
}

/// <summary>
/// A temporary directory holding the SQLite files of a single test
/// </summary>
internal sealed class TempDatabase : IDisposable {
  private readonly string _directory;

  /// <summary>Path of the database file; it doesn't exist until a server opens it</summary>
  public string Path { get; }

  public TempDatabase() {
    _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"openpolytopia-it-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_directory);
    Path = System.IO.Path.Combine(_directory, "server.db");
  }

  public void Dispose() {
    try {
      Directory.Delete(_directory, true);
    }
    catch (IOException) {
      // a leftover wal file on a slow filesystem isn't worth failing a test over
    }
  }
}

/// <summary>
/// Helpers shared by the integration tests: names, credentials and the packet sequence that starts a real game
/// </summary>
internal static class TestGames {
  /// <summary>Smallest world a lobby accepts, so the tests generate the map as fast as possible</summary>
  public const uint WORLD_SIZE = 11;

  /// <summary>Passwords have a 12 characters minimum, every test account shares this one</summary>
  public const string PASSWORD = "correct horse battery";

  private static int _counter;

  /// <summary>Builds a username unique across the whole test run</summary>
  public static string UniqueName(string prefix) => $"{prefix}_{Interlocked.Increment(ref _counter)}";

  /// <summary>
  /// Creates a two player lobby, readies both players and waits for the game the server starts out of it
  /// </summary>
  /// <remarks>
  /// The server only looks for ready lobbies every 5 seconds, so the wait here has to outlast a full tick plus
  /// the map generation
  /// </remarks>
  /// <returns>the id of the started game, i.e. the id of the lobby</returns>
  public static async Task<ulong> StartGameAsync(TestClient host, TestClient guest) {
    await host.SendAsync(new CreateLobbyPacket { MaxPlayers = 2, WorldSize = WORLD_SIZE, Tribe = 0 });
    var created = await host.ExpectAsync<CreateLobbyResponsePacket>();
    created.Result.ShouldBe(LobbyActionResult.Ok);

    // tribes.json only ships imperius, and the server refuses a lobby with a tribe it has no data for
    await guest.SendAsync(new JoinLobbyPacket { LobbyId = created.LobbyId, Tribe = 0 });
    (await guest.ExpectAsync<JoinLobbyResponsePacket>()).Result.ShouldBe(LobbyActionResult.Ok);

    await host.SendAsync(new SetReadyPacket { LobbyId = created.LobbyId, Ready = true });
    (await host.ExpectAsync<SetReadyResponsePacket>()).Result.ShouldBe(LobbyActionResult.Ok);
    await guest.SendAsync(new SetReadyPacket { LobbyId = created.LobbyId, Ready = true });
    (await guest.ExpectAsync<SetReadyResponsePacket>()).Result.ShouldBe(LobbyActionResult.Ok);

    var startTimeout = TimeSpan.FromSeconds(60);
    foreach (var client in new[] { host, guest }) {
      await client.ExpectAsync<GameStartedPacket>(packet => packet.LobbyId == created.LobbyId, startTimeout);
      await client.ExpectAsync<GameStatePacket>(packet => packet.GameId == created.LobbyId, startTimeout);
    }

    return created.LobbyId;
  }

  /// <summary>
  /// Asks the server for the full state of a game the client is a member of
  /// </summary>
  public static async Task<GameStatePacket> GetStateAsync(this TestClient client, ulong gameId) {
    await client.SendAsync(new GetGameStatePacket { GameId = gameId });
    return await client.ExpectAsync<GameStatePacket>(packet => packet.GameId == gameId);
  }

  /// <summary>
  /// Finds every tile holding a troop of a player, as grid positions
  /// </summary>
  public static List<Godot.Vector2I> TroopsOf(this GameStatePacket state, uint playerId) {
    List<Godot.Vector2I> positions = [];

    for (var index = 0; index < state.Troops.Length; index++) {
      var troop = new Common.TroopData { Raw = state.Troops[index] };
      if (troop.IsValid() && troop.Player == playerId) {
        positions.Add(new Godot.Vector2I(index % (int)state.WorldSize, index / (int)state.WorldSize));
      }
    }

    return positions;
  }

  /// <summary>Reads the packed troop of a position out of a full state packet</summary>
  public static Common.TroopData TroopAt(this GameStatePacket state, Godot.Vector2I position) =>
    new() { Raw = state.Troops[(position.Y * (int)state.WorldSize) + position.X] };
}
