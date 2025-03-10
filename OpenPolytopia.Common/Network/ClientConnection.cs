namespace OpenPolytopia.Common.Network;

using System.Net.Sockets;
using DotNext.Threading;
using Packets;

public class ClientConnection : NetworkConnection {
  public delegate Task HandshakeResponse(bool result);

  public delegate Task LobbiesResponse(Lobby[] lobbies);

  public delegate Task CreateLobbyResponse(uint id);

  public delegate Task LobbyDeleted(uint id);

  public delegate Task LobbyConnectResponse(bool result);

  public delegate Task LobbyUpdate(Lobby lobby);

  public event HandshakeResponse? OnHandshakeResponse;

  /// <summary>
  /// Fired after querying the server for lobbies
  /// </summary>
  public event LobbiesResponse? OnLobbiesResponse;

  /// <summary>
  /// Fired after asking the server to create a lobby
  /// </summary>
  public event CreateLobbyResponse? OnCreateLobbyResponse;

  /// <summary>
  /// Fired when a lobby gets deleted
  /// </summary>
  public event LobbyDeleted? OnLobbyDeleted;

  /// <summary>
  /// Fired after asking the server to join a lobby
  /// </summary>
  public event LobbyConnectResponse? OnLobbyConnect;

  /// <summary>
  /// Fired when a lobby gets modified
  /// </summary>
  public event LobbyUpdate? OnLobbyUpdate;

  private readonly string _address;
  private readonly int _port;
  private readonly CancellationToken _ct;
  private readonly TcpClient _tcpClient;
  private Task? _clientTask;

  /// <summary>
  /// Underlying stream with the server
  /// </summary>
  public NetworkStream? Stream { get; private set; }

  public ClientConnection(string address, int port, CancellationToken ct) {
    _address = address;
    _port = port;
    _ct = ct;
    _tcpClient = new TcpClient(address, port);
    OnPacketReceived += PacketReceivedAsync;
  }

  private async Task<bool> PacketReceivedAsync(uint id, IPacket packet, NetworkStream stream, List<byte> bytes) {
    Stream ??= stream;

    switch (packet) {
      case HandshakeResponsePacket handshakeResponsePacket:
        var handshakeResult = OnHandshakeResponse?.BeginInvoke(handshakeResponsePacket.Ok, null, null);
        if (handshakeResult == null) {
          return true;
        }

        await handshakeResult.AsyncWaitHandle.WaitAsync(_ct);
        break;
      case GetLobbiesResponsePacket lobbiesResponsePacket:
        var lobbiesResponseResult = OnLobbiesResponse?.BeginInvoke(lobbiesResponsePacket.Lobbies.ToArray(), null, null);
        if (lobbiesResponseResult == null) {
          break;
        }

        await lobbiesResponseResult.AsyncWaitHandle.WaitAsync(_ct);
        break;
      case CreateLobbyResponsePacket createLobbyResponsePacket:
        var createLobbyResult = OnCreateLobbyResponse?.BeginInvoke(createLobbyResponsePacket.Id, null, null);
        if (createLobbyResult == null) {
          break;
        }

        await createLobbyResult.AsyncWaitHandle.WaitAsync(_ct);
        break;
      case LobbyDeletedPacket lobbyDeletedPacket:
        var lobbyDeletedResult = OnLobbyDeleted?.BeginInvoke(lobbyDeletedPacket.Id, null, null);
        if (lobbyDeletedResult == null) {
          break;
        }

        await lobbyDeletedResult.AsyncWaitHandle.WaitAsync(_ct);
        break;
      case LobbyConnectResponsePacket lobbyConnectResponsePacket:
        var lobbyConnectResult = OnLobbyConnect?.BeginInvoke(lobbyConnectResponsePacket.Ok, null, null);
        if (lobbyConnectResult == null) {
          break;
        }

        await lobbyConnectResult.AsyncWaitHandle.WaitAsync(_ct);
        break;
      case LobbyUpdatePacket lobbyUpdatePacket:
        var lobbyUpdateResult = OnLobbyUpdate?.BeginInvoke(lobbyUpdatePacket.Lobby, null, null);
        if (lobbyUpdateResult == null) {
          break;
        }

        await lobbyUpdateResult.AsyncWaitHandle.WaitAsync(_ct);
        break;
    }

    return false;
  }

  /// <summary>
  /// Initializes the connection to the server
  /// </summary>
  public async Task ConnectAsync() {
    await _tcpClient.ConnectAsync(_address, _port, _ct);
    _clientTask = ManageClientAsync(0, _tcpClient, _ct);
  }

  protected override async Task ManageKeepAlivePacketAsync(KeepAlivePacket packet, NetworkStream stream,
    List<byte> bytes) => await stream.WritePacketAsync(packet, bytes);
}
