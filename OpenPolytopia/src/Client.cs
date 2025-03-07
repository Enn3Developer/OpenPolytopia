namespace OpenPolytopia;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Network;
using Common.Network.Packets;
using DotNext.Threading;

public class Client {
  public static Client Instance { get; private set; }

  public delegate Task Connected();

  /// <summary>
  /// Fired when the client successfully connects to the server
  /// </summary>
  public event Connected? OnConnected;

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
    Instance = this;
  }

  public async Task ConnectAsync() => await ClientConnection.ConnectAsync();

  public async Task RegisterUserAsync(string name, List<byte>? bytes = null) {
    bytes ??= new List<byte>(16);
    if (ClientConnection.Stream != null) {
      await ClientConnection.Stream.WritePacketAsync(new RegisterUserPacket { Name = name }, bytes);
    }
  }

  public async Task GetLobbiesAsync(List<byte>? bytes = null) {
    bytes ??= new List<byte>(16);
    if (ClientConnection.Stream != null) {
      await ClientConnection.Stream.WritePacketAsync(new GetLobbiesPacket(), bytes);
    }
  }

  public async Task CreateLobbyAsync(uint maxPlayer, List<byte>? bytes = null) {
    bytes ??= new List<byte>(16);
    if (ClientConnection.Stream != null) {
      await ClientConnection.Stream.WritePacketAsync(new CreateLobbyPacket { MaxPlayers = maxPlayer }, bytes);
    }
  }

  public async Task LobbyConnectAsync(uint lobbyId, List<byte>? bytes = null) {
    bytes ??= new List<byte>(16);
    if (ClientConnection.Stream != null) {
      await ClientConnection.Stream.WritePacketAsync(new LobbyConnectPacket { Id = lobbyId }, bytes);
    }
  }

  public async Task LobbyDisconnectAsync(uint lobbyId, List<byte>? bytes = null) {
    bytes ??= new List<byte>(16);
    if (ClientConnection.Stream != null) {
      await ClientConnection.Stream.WritePacketAsync(new LobbyDisconnectPacket { Id = lobbyId }, bytes);
    }
  }
}
