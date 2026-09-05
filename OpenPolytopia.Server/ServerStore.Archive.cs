namespace OpenPolytopia.Server;

using System.Globalization;
using Microsoft.Data.Sqlite;

/// <summary>A completed match and its account membership, stored outside the live snapshot.</summary>
public sealed record ArchivedGame(ulong Id, string State, IReadOnlyCollection<uint> Accounts);

public sealed partial class ServerStore {
  private void SaveCompletedGames(SqliteTransaction transaction, IReadOnlyList<ArchivedGame>? games) {
    if (games == null) return;
    foreach (var game in games) {
      var id = game.Id.ToString(CultureInfo.InvariantCulture);
      using var command = _connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText = "INSERT INTO completed_games (id, state) VALUES ($id, $state) ON CONFLICT(id) DO UPDATE SET state = excluded.state";
      command.Parameters.AddWithValue("$id", id);
      command.Parameters.AddWithValue("$state", game.State);
      command.ExecuteNonQuery();
      foreach (var account in game.Accounts) {
        using var member = _connection.CreateCommand();
        member.Transaction = transaction;
        member.CommandText = "INSERT OR IGNORE INTO completed_game_members (game_id, account_id) VALUES ($id, $account)";
        member.Parameters.AddWithValue("$id", id);
        member.Parameters.AddWithValue("$account", account);
        member.ExecuteNonQuery();
      }
    }
  }

  /// <summary>Reads a completed match only when the account is one of its members.</summary>
  public string? LoadCompletedGame(ulong gameId, uint accountId) {
    lock (_lock) {
      ThrowIfDisposed();
      using var command = _connection.CreateCommand();
      command.CommandText = "SELECT state FROM completed_games JOIN completed_game_members ON id = game_id WHERE id = $id AND account_id = $account";
      command.Parameters.AddWithValue("$id", gameId.ToString(CultureInfo.InvariantCulture));
      command.Parameters.AddWithValue("$account", accountId);
      return command.ExecuteScalar() as string;
    }
  }

  /// <summary>Lists a member's completed matches without loading their world snapshots.</summary>
  public IReadOnlyList<ulong> CompletedGameIds(uint accountId) {
    lock (_lock) {
      ThrowIfDisposed();
      using var command = _connection.CreateCommand();
      command.CommandText = "SELECT game_id FROM completed_game_members WHERE account_id = $account";
      command.Parameters.AddWithValue("$account", accountId);
      using var reader = command.ExecuteReader();
      var ids = new List<ulong>();
      while (reader.Read()) ids.Add(ulong.Parse(reader.GetString(0), CultureInfo.InvariantCulture));
      return ids;
    }
  }
}
