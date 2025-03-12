namespace OpenPolytopia.Server;

using SpacetimeDB;

public static partial class Module {
  /// <summary>
  /// Defines a player
  /// </summary>
  [Table(Name = "Player", Public = true)]
  public partial class Player {
    [PrimaryKey] public Identity Id;
    public required string Name;
    public bool Online;
  }

  /// <summary>
  /// Defines a lobby where players can join and start a game
  /// </summary>
  [Table(Name = "Lobby", Public = true)]
  public partial class Lobby {
    [PrimaryKey] [AutoInc] public ulong Id;
    public uint MaxPlayers;
    public uint Players;
    public uint Ready;
    public bool Started;
    public bool Starting;
  }

  /// <summary>
  /// Defines a player who joined a lobby
  /// </summary>
  [Table(Name = "LobbyPlayer", Public = true)]
  [Index.BTree(Name = "LobbyAndPlayer", Columns = [nameof(LobbyId), nameof(PlayerId)])]
  public partial class LobbyPlayer {
    [PrimaryKey] [AutoInc] public ulong Id;
    public ulong LobbyId;
    public Identity PlayerId;
    public uint Tribe;
  }

  [Reducer(ReducerKind.ClientConnected)]
  public static void ClientConnected(ReducerContext ctx) {
    // get the player
    var player = ctx.FindPlayer();

    // check if he exists in the database
    if (player == null) {
      throw new UserNotRegisteredException();
    }

    // set the online status
    player.Online = true;

    // update the database
    ctx.Db.Player.Id.Update(player);
  }

  [Reducer(ReducerKind.ClientDisconnected)]
  public static void ClientDisconnected(ReducerContext ctx) {
    // get the player
    var player = ctx.FindPlayer();

    // check if he exists in the database
    if (player == null) {
      throw new UserNotRegisteredException();
    }

    // set the online status
    player.Online = false;

    // update the database
    ctx.Db.Player.Id.Update(player);
  }

  [Reducer]
  public static void SetName(ReducerContext ctx, string name) {
    // get the player
    var player = ctx.FindPlayer();

    // check if player exists
    if (player != null) {
      // set the name
      player.Name = name;

      // and update him
      ctx.Db.Player.Id.Update(player);
    }
    else {
      // create a new player; we set the online status to true because to make changes to the name you need to be online
      player = new Player { Id = ctx.Sender, Name = name, Online = true, };

      // and add him
      ctx.Db.Player.Insert(player);
    }
  }

  [Reducer]
  public static void CreateLobby(ReducerContext ctx, uint maxPlayers, uint tribe) {
    // get the player
    var player = ctx.FindPlayer();

    // check if the player exists
    if (player == null) {
      throw new UserNotRegisteredException();
    }

    // create the lobby
    var lobby = ctx.CreateLobby(maxPlayers);

    // set the players in lobby to 1
    lobby.Players++;

    // create the lobby player
    ctx.CreateLobbyPlayer(lobby.Id, tribe);
  }

  [Reducer]
  public static void JoinLobby(ReducerContext ctx, ulong lobbyId, uint tribe) {
    // get the player
    var player = ctx.FindPlayer();

    // check if the player exists
    if (player == null) {
      throw new UserNotRegisteredException();
    }

    // get the lobby
    var lobby = ctx.FindLobby(lobbyId);

    // check if lobby exists
    if (lobby == null) {
      throw new LobbyNotFoundException();
    }

    // check if lobby is still waiting for more players
    if (lobby.Starting || lobby.Started) {
      throw new LobbyAlreadyStartedException();
    }

    // check if the lobby is full
    if (lobby.Players >= lobby.MaxPlayers) {
      throw new LobbyFullException();
    }

    // get all players in the lobby
    var lobbyPlayers = ctx.FilterLobbyPlayer(lobbyId);

    // check if player is in lobby
    if (lobbyPlayers.Any(lobbyPlayer => lobbyPlayer.PlayerId == ctx.Sender)) {
      throw new AlreadyJoinedLobbyException();
    }

    // increment the players amount
    lobby.Players++;

    // update the lobby
    ctx.UpdateLobby(lobby);

    // add the player to the lobby
    ctx.CreateLobbyPlayer(lobbyId, tribe);
  }

  [Reducer]
  public static void LeaveLobby(ReducerContext ctx, ulong lobbyId) {
    // get the player
    var player = ctx.FindPlayer();

    // check if the player exists
    if (player == null) {
      throw new UserNotRegisteredException();
    }

    // get the lobby
    var lobby = ctx.FindLobby(lobbyId);

    // check if lobby exists
    if (lobby == null) {
      throw new LobbyNotFoundException();
    }

    // check if lobby is still waiting for more players
    if (lobby.Starting || lobby.Started) {
      throw new LobbyAlreadyStartedException();
    }

    // get all players in the lobby
    var lobbyPlayer = ctx.FilterLobbyPlayer(lobbyId).FirstOrDefault(lobbyPlayer => lobbyPlayer.PlayerId == ctx.Sender);

    // check if the player is in the lobby
    if (lobbyPlayer == null) {
      throw new NotInLobbyException();
    }

    // remove the player from the lobby
    ctx.RemoveLobbyPlayer(lobbyPlayer);
  }

  [Reducer]
  public static void AddReady(ReducerContext ctx, ulong lobbyId) {
    // get the player
    var player = ctx.FindPlayer();

    // check if the player exists
    if (player == null) {
      throw new UserNotRegisteredException();
    }

    // get the lobby
    var lobby = ctx.FindLobby(lobbyId);

    // check if lobby exists
    if (lobby == null) {
      throw new LobbyNotFoundException();
    }

    // check if lobby is still waiting for more players
    if (lobby.Starting || lobby.Started) {
      throw new LobbyAlreadyStartedException();
    }

    // get all players in the lobby
    var lobbyPlayer = ctx.FilterLobbyPlayer(lobbyId).FirstOrDefault(lobbyPlayer => lobbyPlayer.PlayerId == ctx.Sender);

    // check if the player is in the lobby
    if (lobbyPlayer == null) {
      throw new NotInLobbyException();
    }

    // increment the number of players ready
    lobby.Ready++;

    // check if all players are ready
    if (lobby.Ready == lobby.Players) {
      // start the game
      lobby.Starting = true;
    }

    // update the lobby
    ctx.UpdateLobby(lobby);
  }

  [Reducer]
  public static void RemoveReady(ReducerContext ctx, ulong lobbyId) {
    // get the player
    var player = ctx.FindPlayer();

    // check if the player exists
    if (player == null) {
      throw new UserNotRegisteredException();
    }

    // get the lobby
    var lobby = ctx.FindLobby(lobbyId);

    // check if lobby exists
    if (lobby == null) {
      throw new LobbyNotFoundException();
    }

    // check if lobby is still waiting for more players
    if (lobby.Starting || lobby.Started) {
      throw new LobbyAlreadyStartedException();
    }

    // get all players in the lobby
    var lobbyPlayer = ctx.FilterLobbyPlayer(lobbyId).FirstOrDefault(lobbyPlayer => lobbyPlayer.PlayerId == ctx.Sender);

    // check if the player is in the lobby
    if (lobbyPlayer == null) {
      throw new NotInLobbyException();
    }

    // decrement the number of players ready
    lobby.Ready--;

    // update the lobby
    ctx.UpdateLobby(lobby);
  }
}
