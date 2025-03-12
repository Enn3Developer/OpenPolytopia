namespace OpenPolytopia;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;

public partial class SpacetimeNode : Node {
  public static SpacetimeNode Instance { get; private set; } = null!;

  private const string HOST = "https://spacetime.enn3.ovh";
  private const string DBNAME = "openpolytopia";

  private Identity _identity;
  public DbConnection Connection = null!;
  public readonly ObservableCollection<LobbyData> Lobbies = [];

  public override void _EnterTree() {
    base._EnterTree();
    Instance = this;
    AuthToken.Init(".stdb_openpolytopia");
    Connection = ConnectToDb();
    RegisterCallbacks();
  }

  public override void _ExitTree() {
    base._ExitTree();
    Connection.Disconnect();
  }

  public override void _PhysicsProcess(double delta) => Connection.FrameTick();

  private void RegisterCallbacks() {
    Connection.Db.Lobby.OnInsert += (context, row) => {
      Lobbies.Add(
        new LobbyData { Id = row.Id, MaxPlayers = row.MaxPlayers, Players = row.Players, Ready = row.Ready });
    };

    Connection.Db.Lobby.OnDelete += (context, row) => {
      var data = Lobbies.FirstOrDefault(data => data.Id == row.Id);
      if (data == null) {
        return;
      }

      Lobbies.Remove(data);
    };

    Connection.Db.Lobby.OnUpdate += (context, row, newRow) => {
      var data = Lobbies.FirstOrDefault(data => data.Id == row.Id);
      if (data != null) {
        Lobbies.Remove(data);
      }

      Lobbies.Add(
        new LobbyData {
          Id = newRow.Id, MaxPlayers = newRow.MaxPlayers, Players = newRow.Players, Ready = newRow.Ready
        });
    };
  }

  private DbConnection ConnectToDb() {
    var conn = DbConnection.Builder()
      .WithUri(HOST)
      .WithModuleName(DBNAME)
      .WithToken(AuthToken.Token)
      .OnConnect(OnConnected)
      .OnConnectError(OnConnectError)
      .OnDisconnect(OnDisconnected)
      .Build();
    return conn;
  }

  private void OnConnected(DbConnection conn, Identity identity, string authToken) {
    AuthToken.SaveToken(authToken);
    _identity = identity;

    conn.SubscriptionBuilder().OnApplied(context => { }).OnError((context, exception) => { }).Subscribe([
      // get lobbies the player joined
      $"SELECT Lobby.* FROM Lobby JOIN LobbyPlayer ON Lobby.Id = LobbyPlayer.LobbyId WHERE LobbyPlayer.PlayerId = 0x{identity}",
      "SELECT * FROM Player",
      "SELECT * FROM LobbyPlayer"
    ]);
  }

  private void OnConnectError(Exception e) {
    GD.PushError($"Error while connecting: {e}");
  }

  private void OnDisconnected(DbConnection conn, Exception? e) {
    if (e != null) {
      GD.PushError($"Disconnected abnormally: {e}");
    }
    else {
      GD.Print($"Disconnected normally.");
    }
  }
}

public class LobbyData {
  public ulong Id { get; init; }
  public uint MaxPlayers;
  public uint Players;
  public uint Ready;
}
