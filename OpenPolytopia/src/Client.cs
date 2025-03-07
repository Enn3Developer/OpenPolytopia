namespace OpenPolytopia;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Network;
using Common.Network.Packets;
using DotNext.Threading;

public class Client {
  /// <summary>
  /// Public instance of the client
  /// </summary>
  /// <remarks>
  /// Remember to initialize the client at least once
  /// </remarks>
  public static Client? Instance { get; private set; }

  public delegate Task Connected();

  /// <summary>
  /// Fired when the client successfully connects to the server
  /// </summary>
  public event Connected? OnConnected;

  /// <summary>
  /// The underlying connection which manages messages to/from the server
  /// </summary>
  public ClientConnection ClientConnection { get; }

  public Client(string address, int port, CancellationToken ct) {
    ClientConnection = new ClientConnection(address, port, ct);
    ClientConnection.OnHandshakeResponse += async handshakeResult => {
      if (OnConnected == null || !handshakeResult) {
        return;
      }

      var result = OnConnected.BeginInvoke(null, null);
      await result.AsyncWaitHandle.WaitAsync();
    };
    Instance ??= this;
  }

  /// <summary>
  /// Initializes the connection to the server
  /// </summary>
  public async Task ConnectAsync() => await ClientConnection.ConnectAsync();

  /// <summary>
  /// Registers the user on the server
  /// </summary>
  /// <param name="name">name of the player</param>
  /// <param name="bytes">optional list to use instead of allocating a new one</param>
  public async Task RegisterUserAsync(string name, List<byte>? bytes = null) {
    bytes ??= new List<byte>(16);
    if (ClientConnection.Stream != null) {
      await ClientConnection.Stream.WritePacketAsync(new RegisterUserPacket { Name = name }, bytes);
    }
  }

  /// <summary>
  /// Query the server for all the lobbies
  /// </summary>
  /// <param name="bytes">optional list to use instead of allocating a new one</param>
  public async Task GetLobbiesAsync(List<byte>? bytes = null) {
    bytes ??= new List<byte>(16);
    if (ClientConnection.Stream != null) {
      await ClientConnection.Stream.WritePacketAsync(new GetLobbiesPacket(), bytes);
    }
  }

  /// <summary>
  /// Creates a new lobby on the server
  /// </summary>
  /// <param name="maxPlayer">the number of players</param>
  /// <param name="bytes">optional list to use instead of allocating a new one</param>
  public async Task CreateLobbyAsync(uint maxPlayer, List<byte>? bytes = null) {
    bytes ??= new List<byte>(16);
    if (ClientConnection.Stream != null) {
      await ClientConnection.Stream.WritePacketAsync(new CreateLobbyPacket { MaxPlayers = maxPlayer }, bytes);
    }
  }

  /// <summary>
  /// Connects to the lobby
  /// </summary>
  /// <param name="lobbyId">the id of the lobby to connect to</param>
  /// <param name="bytes">optional list to use instead of allocating a new one</param>
  public async Task LobbyConnectAsync(uint lobbyId, List<byte>? bytes = null) {
    bytes ??= new List<byte>(16);
    if (ClientConnection.Stream != null) {
      await ClientConnection.Stream.WritePacketAsync(new LobbyConnectPacket { Id = lobbyId }, bytes);
    }
  }

  /// <summary>
  /// Disconnects from a lobby
  /// </summary>
  /// <param name="lobbyId">the id of the lobby to disconnect from</param>
  /// <param name="bytes">optional list to use instead of allocating a new one</param>
  public async Task LobbyDisconnectAsync(uint lobbyId, List<byte>? bytes = null) {
    bytes ??= new List<byte>(16);
    if (ClientConnection.Stream != null) {
      await ClientConnection.Stream.WritePacketAsync(new LobbyDisconnectPacket { Id = lobbyId }, bytes);
    }
  }
}
