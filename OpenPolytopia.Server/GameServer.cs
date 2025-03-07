namespace OpenPolytopia.Server;

using System.Collections.Concurrent;
using System.Net.Sockets;
using Common;
using Common.Network;
using Common.Network.Packets;

public class GameServer : IDisposable {
  private readonly ServerConnection _server;
  private readonly LobbyManager _lobbyManager = new();
  private readonly ConcurrentDictionary<uint, string> _playerNames = new();

  public GameServer(int port) {
    _server = new ServerConnection(port);
    PacketRegistrar.RegisterAllPackets();
    _server.OnPacketReceived += ManagePacketAsync;
  }

  private async Task<bool> ManagePacketAsync(uint id, IPacket packet, NetworkStream stream, List<byte> bytes) {
    switch (packet) {
      // Handshake packet
      // Respond with the result of the check
      case HandshakePacket handshakePacket:
        await stream.WritePacketAsync(new HandshakeResponsePacket { Ok = handshakePacket.Version == "0.1.0" }, bytes);
        break;
      // Register the username
      case RegisterUserPacket registerUserPacket:
        foreach (var keyValue in _playerNames) {
          if (keyValue.Value == registerUserPacket.Name) {
            _playerNames.TryRemove(keyValue.Key, out _);
          }
        }

        _playerNames[id] = registerUserPacket.Name;
        await stream.WritePacketAsync(new RegisterUserResponsePacket { Ok = true }, bytes);
        break;
      // Add a player to a lobby
      // Respond with the result of the operation
      case LobbyConnectPacket lobbyConnectPacket:
        var ok = _lobbyManager.AddPlayer(lobbyConnectPacket.Id, _playerNames[id]);
        if (ok) {
          Broadcast(new LobbyUpdatePacket { Lobby = _lobbyManager[id]! });
        }

        await stream.WritePacketAsync(new LobbyConnectResponsePacket { Ok = ok }, bytes);
        break;
      // Create a new lobby
      case CreateLobbyPacket createLobbyPacket:
        var lobby = _lobbyManager.NewLobby(createLobbyPacket.MaxPlayers);
        Broadcast(new LobbyUpdatePacket { Lobby = lobby });
        await stream.WritePacketAsync(new CreateLobbyResponsePacket { Ok = true, Id = lobby.Id }, bytes);
        break;
      // Respond with the lobbies currently available
      case GetLobbiesPacket:
        await stream.WritePacketAsync(new GetLobbiesResponsePacket { Lobbies = _lobbyManager.Lobbies }, bytes);
        break;
      // Disconnect from a lobby
      case LobbyDisconnectPacket lobbyDisconnectPacket:
        _lobbyManager.RemovePlayer(lobbyDisconnectPacket.Id, _playerNames[id]);
        if (_lobbyManager[lobbyDisconnectPacket.Id] != null) {
          var lobbyToDelete = _lobbyManager[lobbyDisconnectPacket.Id]!;
          if (lobbyToDelete.Players.Count == 0) {
            _lobbyManager.Lobbies.Remove(lobbyToDelete);
            Broadcast(new LobbyDeletedPacket { Id = lobbyDisconnectPacket.Id });
          }
        }

        await stream.WritePacketAsync(new LobbyDisconnectResponsePacket { Ok = true }, bytes);
        break;
    }

    return false;
  }

  private void Broadcast(IPacket packet) {
    foreach (var clientId in _playerNames.Keys) {
      _server.Channels[clientId].Enqueue(packet);
    }
  }

  private void BroadcastTo(uint[] ids, IPacket packet) {
    foreach (var id in ids) {
      _server.Channels[id].Enqueue(packet);
    }
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
