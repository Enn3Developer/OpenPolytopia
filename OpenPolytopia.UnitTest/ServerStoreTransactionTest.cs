namespace OpenPolytopia;

using System;
using System.Collections.Generic;
using Server;
using Shouldly;

public class ServerStoreTransactionTest {
  [Fact]
  public void RenameAndSnapshotCommitTogether() {
    using var store = new ServerStore(":memory:");
    var login = store.Register("alice", "correct horse battery");
    store.SaveState("updated lobbies", new Dictionary<uint, string> { [login.Account.Id] = "New name" });
    store.Resume(login.Token)!.DisplayName.ShouldBe("New name");
    store.LoadState().ShouldBe("updated lobbies");
  }

  [Fact]
  public void FailedRenameRollsBackSnapshot() {
    using var store = new ServerStore(":memory:");
    store.SaveState("before");
    Should.Throw<ArgumentException>(() => store.SaveState("after", new Dictionary<uint, string> { [123] = "Unknown" }));
    store.LoadState().ShouldBe("before");
  }
}
