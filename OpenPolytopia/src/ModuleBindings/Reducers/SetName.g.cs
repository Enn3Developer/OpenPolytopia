// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using SpacetimeDB.ClientApi;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types {
  public sealed partial class RemoteReducers : RemoteBase {
    public delegate void SetNameHandler(ReducerEventContext ctx, string name);
    public event SetNameHandler? OnSetName;

    public void SetName(string name) {
      conn.InternalCallReducer(new Reducer.SetName(name), this.SetCallReducerFlags.SetNameFlags);
    }

    public bool InvokeSetName(ReducerEventContext ctx, Reducer.SetName args) {
      if (OnSetName == null)
        return false;
      OnSetName(
          ctx,
          args.Name
      );
      return true;
    }
  }

  public abstract partial class Reducer {
    [SpacetimeDB.Type]
    [DataContract]
    public sealed partial class SetName : Reducer, IReducerArgs {
      [DataMember(Name = "name")]
      public string Name;

      public SetName(string Name) {
        this.Name = Name;
      }

      public SetName() {
        this.Name = "";
      }

      string IReducerArgs.ReducerName => "SetName";
    }
  }

  public sealed partial class SetReducerFlags {
    internal CallReducerFlags SetNameFlags;
    public void SetName(CallReducerFlags flags) => SetNameFlags = flags;
  }
}
