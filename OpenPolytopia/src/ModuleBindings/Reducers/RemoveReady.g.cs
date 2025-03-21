// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using SpacetimeDB.ClientApi;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types {
  public sealed partial class RemoteReducers : RemoteBase {
    public delegate void RemoveReadyHandler(ReducerEventContext ctx, ulong lobbyId);
    public event RemoveReadyHandler? OnRemoveReady;

    public void RemoveReady(ulong lobbyId) {
      conn.InternalCallReducer(new Reducer.RemoveReady(lobbyId), this.SetCallReducerFlags.RemoveReadyFlags);
    }

    public bool InvokeRemoveReady(ReducerEventContext ctx, Reducer.RemoveReady args) {
      if (OnRemoveReady == null)
        return false;
      OnRemoveReady(
          ctx,
          args.LobbyId
      );
      return true;
    }
  }

  public abstract partial class Reducer {
    [SpacetimeDB.Type]
    [DataContract]
    public sealed partial class RemoveReady : Reducer, IReducerArgs {
      [DataMember(Name = "lobbyId")]
      public ulong LobbyId;

      public RemoveReady(ulong LobbyId) {
        this.LobbyId = LobbyId;
      }

      public RemoveReady() {
      }

      string IReducerArgs.ReducerName => "RemoveReady";
    }
  }

  public sealed partial class SetReducerFlags {
    internal CallReducerFlags RemoveReadyFlags;
    public void RemoveReady(CallReducerFlags flags) => RemoveReadyFlags = flags;
  }
}
