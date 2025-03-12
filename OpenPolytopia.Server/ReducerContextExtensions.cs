namespace OpenPolytopia.Server;

using System.Runtime.CompilerServices;
using SpacetimeDB;

public static class ReducerContextExtensions {
  public static Module.Player? FindPlayer(this ReducerContext ctx, Identity? id = null) =>
    ctx.Db.Player.Id.Find(id ?? ctx.Sender);

  public static Module.Lobby? FindLobby(this ReducerContext ctx, ulong id) => ctx.Db.Lobby.Id.Find(id);

  public static Module.LobbyPlayer? FindLobbyPlayer(this ReducerContext ctx, ulong id) =>
    ctx.Db.LobbyPlayer.Id.Find(id);

  public static IEnumerable<Module.LobbyPlayer> FilterLobbyPlayer(this ReducerContext ctx, ulong lobbyId,
    Identity? playerId = null) =>
    playerId == null
      ? ctx.Db.LobbyPlayer.LobbyAndPlayer.Filter(lobbyId)
      : ctx.Db.LobbyPlayer.LobbyAndPlayer.Filter((lobbyId, playerId.Value));

  public static Module.Lobby CreateLobby(this ReducerContext ctx, uint maxPlayers) =>
    ctx.Db.Lobby.Insert(new Module.Lobby {
      Id = 0,
      MaxPlayers = maxPlayers,
      Started = false,
      Starting = false,
      Players = 0,
      Ready = 0
    });

  public static Module.Lobby UpdateLobby(this ReducerContext ctx, Module.Lobby lobby) => ctx.Db.Lobby.Id.Update(lobby);

  public static void RemoveLobby(this ReducerContext ctx, Module.Lobby lobby) => ctx.Db.Lobby.Id.Delete(lobby.Id);

  public static Module.LobbyPlayer CreateLobbyPlayer(this ReducerContext ctx, ulong lobbyId, uint tribe,
    Identity? id = null) =>
    ctx.Db.LobbyPlayer.Insert(new Module.LobbyPlayer {
      Id = 0, LobbyId = lobbyId, Tribe = tribe, PlayerId = id ?? ctx.Sender
    });

  public static void RemoveLobbyPlayer(this ReducerContext ctx, Module.LobbyPlayer lobbyPlayer) =>
    ctx.Db.LobbyPlayer.Id.Delete(lobbyPlayer.Id);

  public static void FilterRemoveLobbyPlayer(this ReducerContext ctx, Module.Lobby lobby) =>
    ctx.Db.LobbyPlayer.LobbyAndPlayer.Delete(lobby.Id);
}
