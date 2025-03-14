namespace OpenPolytopia;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;

public partial class SpacetimeNode : Node {
  /// <summary>
  /// Public singleton
  /// </summary>
  public static SpacetimeNode Instance { get; private set; } = null!;

  private const string HOST = "https://spacetime.enn3.ovh";
  private const string DBNAME = "openpolytopia";

  private Identity _identity;

  /// <summary>
  /// Accessor to the database connection
  /// </summary>
  public DbConnection Connection { get; private set; } = null!;

  /// <summary>
  /// Lobbies data; it updates
  /// </summary>
  public readonly ObservableCollection<LobbyData> Lobbies = [];

  /// <summary>
  /// Initialize the connection
  /// </summary>
  public override void _EnterTree() {
    base._EnterTree();
    Instance = this;
    AuthToken.Init(".stdb_openpolytopia");
    Connection = ConnectToDb();
    RegisterCallbacks();
  }

  /// <summary>
  /// Disconnect from the database
  /// </summary>
  public override void _ExitTree() {
    base._ExitTree();
    Connection.Disconnect();
  }

  /// <summary>
  /// Process messages
  /// </summary>
  /// <param name="delta">ignored</param>
  public override void _PhysicsProcess(double delta) => Connection.FrameTick();

  /// <summary>
  /// Registering callbacks
  /// </summary>
  private void RegisterCallbacks() {
    // When a lobby gets added (because the player joined)
    Connection.Db.Lobby.OnInsert += (context, row) => {
      // add the lobby to the observable list
      Lobbies.Add(
        new LobbyData { Id = row.Id, MaxPlayers = row.MaxPlayers, Players = row.Players, Ready = row.Ready });
    };

    // When a lobby gets deleted (because the player left the lobby)
    Connection.Db.Lobby.OnDelete += (context, row) => {
      // search for the lobby data in memory
      var data = Lobbies.FirstOrDefault(data => data.Id == row.Id);
      // check if it exists
      if (data == null) {
        return;
      }

      // remove it
      Lobbies.Remove(data);
    };

    // When a lobby gets updated (because another player joined, etc...)
    Connection.Db.Lobby.OnUpdate += (context, row, newRow) => {
      // search for the lobby data in memory
      var data = Lobbies.FirstOrDefault(data => data.Id == row.Id);
      // check if it exists
      if (data != null) {
        // so delete it
        Lobbies.Remove(data);
      }

      // finally, add back the lobby data to force the list to emit the event
      Lobbies.Add(
        new LobbyData {
          Id = newRow.Id, MaxPlayers = newRow.MaxPlayers, Players = newRow.Players, Ready = newRow.Ready
        });
    };
  }

  /// <summary>
  /// Connect to the database
  /// </summary>
  /// <returns>the database connection</returns>
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

    // subscribe with these queries to get updates on the tables
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
