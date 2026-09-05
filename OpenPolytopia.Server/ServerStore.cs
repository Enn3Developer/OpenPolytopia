// cspell:words AUTOINCREMENT
namespace OpenPolytopia.Server;

using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

/// <summary>
/// A registered account, as stored by <see cref="ServerStore"/>
/// </summary>
/// <remarks>
/// Never carries any secret: password material and session tokens never leave the store
/// </remarks>
public sealed record Account {
  /// <summary>
  /// Stable id of the account, never reused once assigned
  /// </summary>
  public required uint Id { get; init; }

  /// <summary>
  /// Normalized (lowercase) username, unique across the store
  /// </summary>
  public required string Username { get; init; }

  /// <summary>
  /// Name to show to other players, the username exactly as it was typed at registration time
  /// </summary>
  public required string DisplayName { get; init; }
}

/// <summary>
/// The result of a successful authentication
/// </summary>
/// <param name="Account">the authenticated account</param>
/// <param name="Token">
/// the raw session token, only ever returned here: the store keeps a hash of it and can't recover it
/// </param>
public sealed record AuthResult(Account Account, string Token);

/// <summary>
/// Persistent server storage: accounts, sessions and the server state snapshot
/// </summary>
/// <remarks>
/// <para>
/// Every API is synchronous and thread-safe; the store serializes all the accesses to the underlying
/// SQLite connection through its own lock
/// </para>
/// <para>
/// Passwords are stored as PBKDF2-HMAC-SHA256 hashes over a per-account random salt, session tokens as
/// SHA-256 hashes of 256 bits of cryptographically random data; neither can be recovered from the database
/// </para>
/// </remarks>
public sealed class ServerStore : IDisposable {
  /// <summary>
  /// Schema version this class knows how to read and write
  /// </summary>
  /// <remarks>
  /// A database with a higher version was written by a newer server and is rejected on open
  /// </remarks>
  public const int SCHEMA_VERSION = 1;

  /// <summary>
  /// Minimum length of a username
  /// </summary>
  public const int MIN_USERNAME_LENGTH = 3;

  /// <summary>
  /// Maximum length of a username
  /// </summary>
  public const int MAX_USERNAME_LENGTH = 32;

  /// <summary>
  /// Minimum length of a password
  /// </summary>
  public const int MIN_PASSWORD_LENGTH = 12;

  /// <summary>
  /// Maximum length of a password
  /// </summary>
  public const int MAX_PASSWORD_LENGTH = 128;

  /// <summary>
  /// PBKDF2 iterations used to derive password hashes
  /// </summary>
  public const int PBKDF2_ITERATIONS = 600_000;

  private const int SALT_SIZE = 16;
  private const int HASH_SIZE = 32;
  private const int TOKEN_SIZE = 32;

  /// <summary>
  /// How long a session stays valid after it's been created
  /// </summary>
  public static TimeSpan SessionLifetime { get; } = TimeSpan.FromDays(30);

  /// <summary>
  /// Salt used to burn the same amount of time on a login for an account that doesn't exist
  /// </summary>
  private static readonly byte[] _dummySalt = new byte[SALT_SIZE];

  private readonly SqliteConnection _connection;
  private readonly TimeProvider _timeProvider;
  private readonly Lock _lock = new();
  private bool _disposed;

  /// <summary>
  /// Opens (creating it if needed) the database at <paramref name="databasePath"/> and migrates it
  /// </summary>
  /// <param name="databasePath">path of the SQLite database file</param>
  /// <param name="timeProvider">
  /// clock used for session creation and expiry, <see cref="TimeProvider.System"/> if <see langword="null"/>
  /// </param>
  /// <exception cref="ArgumentException">if <paramref name="databasePath"/> is empty</exception>
  /// <exception cref="InvalidOperationException">
  /// if the database was written by a newer server, i.e. its schema version is greater than
  /// <see cref="SCHEMA_VERSION"/>
  /// </exception>
  public ServerStore(string databasePath, TimeProvider? timeProvider = null) {
    ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

    _timeProvider = timeProvider ?? TimeProvider.System;
    _connection = new SqliteConnection(new SqliteConnectionStringBuilder {
      DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true, Pooling = false
    }.ToString());
    _connection.Open();

    try {
      Execute("PRAGMA journal_mode = WAL;");
      Execute("PRAGMA foreign_keys = ON;");
      Migrate();
    }
    catch {
      _connection.Dispose();
      throw;
    }
  }

  /// <summary>
  /// Registers a new account and opens a session for it
  /// </summary>
  /// <param name="username">
  /// username to register, 3 to 32 ASCII letters, digits or underscores; it's kept as-is as the display
  /// name and lowercased to check for duplicates
  /// </param>
  /// <param name="password">password of the account, 12 to 128 characters</param>
  /// <returns>the new account and the raw token of its session</returns>
  /// <exception cref="ArgumentException">
  /// if the username or the password don't respect the bounds above, or if an account with the same
  /// normalized username already exists
  /// </exception>
  public AuthResult Register(string username, string password) {
    var normalized = NormalizeUsername(username);
    ValidatePassword(password);

    var salt = RandomNumberGenerator.GetBytes(SALT_SIZE);
    var hash = DeriveKey(password, salt);

    lock (_lock) {
      ThrowIfDisposed();
      using var transaction = _connection.BeginTransaction();

      if (FindAccountByUsername(transaction, normalized) is not null) {
        throw new ArgumentException($"an account named '{normalized}' already exists", nameof(username));
      }

      using var command = _connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText = """
        INSERT INTO accounts (username, display_name, salt, hash, iterations)
        VALUES ($username, $display_name, $salt, $hash, $iterations)
        RETURNING id;
        """;
      command.Parameters.AddWithValue("$username", normalized);
      command.Parameters.AddWithValue("$display_name", username);
      command.Parameters.AddWithValue("$salt", salt);
      command.Parameters.AddWithValue("$hash", hash);
      command.Parameters.AddWithValue("$iterations", PBKDF2_ITERATIONS);

      var account = new Account {
        Id = (uint)(long)command.ExecuteScalar()!, Username = normalized, DisplayName = username
      };
      var token = CreateSession(transaction, account.Id);
      transaction.Commit();
      return new AuthResult(account, token);
    }
  }

  /// <summary>
  /// Authenticates an account with its password and opens a new session for it
  /// </summary>
  /// <param name="username">username of the account, in any casing</param>
  /// <param name="password">password of the account</param>
  /// <returns>
  /// the account and the raw token of the new session, or <see langword="null"/> if the credentials don't
  /// match an account
  /// </returns>
  /// <remarks>
  /// Never tells apart an unknown username from a wrong password, neither by its result nor by how long
  /// it takes to answer
  /// </remarks>
  public AuthResult? Login(string username, string password) {
    ArgumentNullException.ThrowIfNull(username);
    ArgumentNullException.ThrowIfNull(password);

    if (!IsValidUsername(username) || !IsValidPassword(password)) {
      // still burn a derivation so that malformed input isn't faster to reject than a wrong password
      DeriveKey(password.Length == 0 ? "\0" : password, _dummySalt);
      return null;
    }

    var normalized = username.ToLowerInvariant();

    lock (_lock) {
      ThrowIfDisposed();
      using var transaction = _connection.BeginTransaction();

      var record = FindAccountByUsername(transaction, normalized);
      var salt = record?.Salt ?? _dummySalt;
      var iterations = record?.Iterations ?? PBKDF2_ITERATIONS;
      var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HASH_SIZE);

      if (record is null || !CryptographicOperations.FixedTimeEquals(hash, record.Hash)) {
        return null;
      }

      var token = CreateSession(transaction, record.Account.Id);
      transaction.Commit();
      return new AuthResult(record.Account, token);
    }
  }

  /// <summary>
  /// Authenticates a session token handed out by <see cref="Register"/> or <see cref="Login"/>
  /// </summary>
  /// <param name="token">the raw session token</param>
  /// <returns>
  /// the account the session belongs to, or <see langword="null"/> if the token is unknown, malformed,
  /// revoked or expired
  /// </returns>
  /// <remarks>
  /// Expired sessions are dropped from the database when they're hit
  /// </remarks>
  public Account? Resume(string token) {
    if (!TryHashToken(token, out var tokenHash)) {
      return null;
    }

    lock (_lock) {
      ThrowIfDisposed();
      using var transaction = _connection.BeginTransaction();

      using var command = _connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText = """
        SELECT sessions.expires_at, accounts.id, accounts.username, accounts.display_name
        FROM sessions JOIN accounts ON accounts.id = sessions.account_id
        WHERE sessions.token_hash = $token_hash;
        """;
      command.Parameters.AddWithValue("$token_hash", tokenHash);

      Account? account;
      using (var reader = command.ExecuteReader()) {
        if (!reader.Read()) {
          return null;
        }

        if (reader.GetInt64(0) <= _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()) {
          account = null;
        }
        else {
          account = new Account {
            Id = (uint)reader.GetInt64(1), Username = reader.GetString(2), DisplayName = reader.GetString(3)
          };
        }
      }

      if (account is null) {
        DeleteSession(transaction, tokenHash);
      }

      transaction.Commit();
      return account;
    }
  }

  /// <summary>
  /// Revokes a session, making its token useless
  /// </summary>
  /// <param name="token">the raw session token</param>
  /// <returns><see langword="true"/> if a session was revoked, <see langword="false"/> otherwise</returns>
  public bool Logout(string token) {
    if (!TryHashToken(token, out var tokenHash)) {
      return false;
    }

    lock (_lock) {
      ThrowIfDisposed();
      using var transaction = _connection.BeginTransaction();
      var revoked = DeleteSession(transaction, tokenHash) > 0;
      transaction.Commit();
      return revoked;
    }
  }

  /// <summary>
  /// Reads back the server state snapshot saved by <see cref="SaveState"/>
  /// </summary>
  /// <returns>the snapshot, or <see langword="null"/> if none was ever saved</returns>
  public string? LoadState() {
    lock (_lock) {
      ThrowIfDisposed();
      using var command = _connection.CreateCommand();
      command.CommandText = "SELECT state FROM server_state WHERE id = 0;";
      return command.ExecuteScalar() as string;
    }
  }

  /// <summary>
  /// Saves the server state snapshot, replacing the previous one
  /// </summary>
  /// <remarks>
  /// The store treats the snapshot as an opaque blob: what goes in it, and its format, are up to the caller
  /// </remarks>
  /// <param name="state">the JSON snapshot of the server state</param>
  /// <exception cref="ArgumentNullException">if <paramref name="state"/> is <see langword="null"/></exception>
  public void SaveState(string state, IReadOnlyDictionary<uint, string>? renamedAccounts = null) {
    ArgumentNullException.ThrowIfNull(state);

    lock (_lock) {
      ThrowIfDisposed();
      using var transaction = _connection.BeginTransaction();
      using var command = _connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText = """
        INSERT INTO server_state (id, state) VALUES (0, $state)
        ON CONFLICT(id) DO UPDATE SET state = excluded.state;
        """;
      command.Parameters.AddWithValue("$state", state);
      command.ExecuteNonQuery();
      if (renamedAccounts != null) {
        foreach (var (accountId, name) in renamedAccounts) {
          if (name.Length is < 1 or > 32 || name.Any(char.IsControl)) throw new ArgumentException("Invalid display name");
          using var rename = _connection.CreateCommand();
          rename.Transaction = transaction;
          rename.CommandText = "UPDATE accounts SET display_name = $name WHERE id = $id";
          rename.Parameters.AddWithValue("$name", name);
          rename.Parameters.AddWithValue("$id", accountId);
          if (rename.ExecuteNonQuery() != 1) throw new ArgumentException("Unknown account");
        }
      }
      transaction.Commit();
    }
  }

  /// <summary>
  /// Closes the database
  /// </summary>
  public void Dispose() {
    lock (_lock) {
      if (_disposed) {
        return;
      }

      _disposed = true;
      _connection.Dispose();
    }
  }

  #region Schema

  private void Migrate() {
    using var transaction = _connection.BeginTransaction();

    using var version = _connection.CreateCommand();
    version.Transaction = transaction;
    version.CommandText = "PRAGMA user_version;";
    var current = Convert.ToInt64(version.ExecuteScalar());

    if (current > SCHEMA_VERSION) {
      throw new InvalidOperationException(
        $"database schema version {current} is newer than the supported version {SCHEMA_VERSION}");
    }

    if (current == SCHEMA_VERSION) {
      return;
    }

    using var schema = _connection.CreateCommand();
    schema.Transaction = transaction;
    schema.CommandText = $"""
      CREATE TABLE accounts (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        username TEXT NOT NULL UNIQUE,
        display_name TEXT NOT NULL,
        salt BLOB NOT NULL,
        hash BLOB NOT NULL,
        iterations INTEGER NOT NULL
      ) STRICT;

      CREATE TABLE sessions (
        token_hash BLOB PRIMARY KEY,
        account_id INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
        created_at INTEGER NOT NULL,
        expires_at INTEGER NOT NULL
      ) STRICT;

      CREATE INDEX sessions_account_id ON sessions(account_id);

      CREATE TABLE server_state (
        id INTEGER PRIMARY KEY CHECK (id = 0),
        state TEXT NOT NULL
      ) STRICT;

      PRAGMA user_version = {SCHEMA_VERSION};
      """;
    schema.ExecuteNonQuery();
    transaction.Commit();
  }

  private void Execute(string sql) {
    using var command = _connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
  }

  #endregion

  #region Accounts and sessions

  private sealed record AccountRecord(Account Account, byte[] Salt, byte[] Hash, int Iterations);

  private AccountRecord? FindAccountByUsername(SqliteTransaction transaction, string normalized) {
    using var command = _connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = """
      SELECT id, username, display_name, salt, hash, iterations FROM accounts WHERE username = $username;
      """;
    command.Parameters.AddWithValue("$username", normalized);

    using var reader = command.ExecuteReader();
    if (!reader.Read()) {
      return null;
    }

    var account = new Account {
      Id = (uint)reader.GetInt64(0), Username = reader.GetString(1), DisplayName = reader.GetString(2)
    };
    var salt = (byte[])reader.GetValue(3);
    var hash = (byte[])reader.GetValue(4);
    return new AccountRecord(account, salt, hash, reader.GetInt32(5));
  }

  /// <summary>
  /// Inserts a new session for an account and returns its raw token
  /// </summary>
  private string CreateSession(SqliteTransaction transaction, uint accountId) {
    var token = RandomNumberGenerator.GetBytes(TOKEN_SIZE);
    var now = _timeProvider.GetUtcNow();

    using var command = _connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = """
      INSERT INTO sessions (token_hash, account_id, created_at, expires_at)
      VALUES ($token_hash, $account_id, $created_at, $expires_at);
      """;
    command.Parameters.AddWithValue("$token_hash", SHA256.HashData(token));
    command.Parameters.AddWithValue("$account_id", (long)accountId);
    command.Parameters.AddWithValue("$created_at", now.ToUnixTimeMilliseconds());
    command.Parameters.AddWithValue("$expires_at", now.Add(SessionLifetime).ToUnixTimeMilliseconds());
    command.ExecuteNonQuery();

    return Convert.ToBase64String(token);
  }

  private int DeleteSession(SqliteTransaction transaction, byte[] tokenHash) {
    using var command = _connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "DELETE FROM sessions WHERE token_hash = $token_hash;";
    command.Parameters.AddWithValue("$token_hash", tokenHash);
    return command.ExecuteNonQuery();
  }

  #endregion

  #region Validation and crypto

  /// <summary>
  /// Validates a username and returns its normalized form
  /// </summary>
  private static string NormalizeUsername(string username) {
    ArgumentNullException.ThrowIfNull(username);
    if (!IsValidUsername(username)) {
      throw new ArgumentException(
        $"a username must be {MIN_USERNAME_LENGTH} to {MAX_USERNAME_LENGTH} ASCII letters, digits or underscores",
        nameof(username));
    }

    return username.ToLowerInvariant();
  }

  private static void ValidatePassword(string password) {
    ArgumentNullException.ThrowIfNull(password);
    if (!IsValidPassword(password)) {
      throw new ArgumentException(
        $"a password must be {MIN_PASSWORD_LENGTH} to {MAX_PASSWORD_LENGTH} characters long", nameof(password));
    }
  }

  private static bool IsValidUsername(string? username) {
    if (username is null || username.Length is < MIN_USERNAME_LENGTH or > MAX_USERNAME_LENGTH) {
      return false;
    }

    foreach (var character in username) {
      if (!char.IsAsciiLetterOrDigit(character) && character != '_') {
        return false;
      }
    }

    return true;
  }

  private static bool IsValidPassword(string? password) =>
    password is not null && password.Length is >= MIN_PASSWORD_LENGTH and <= MAX_PASSWORD_LENGTH;

  private static byte[] DeriveKey(string password, byte[] salt) =>
    Rfc2898DeriveBytes.Pbkdf2(password, salt, PBKDF2_ITERATIONS, HashAlgorithmName.SHA256, HASH_SIZE);

  /// <summary>
  /// Hashes a raw session token, failing if it isn't a well-formed 256 bits token
  /// </summary>
  private static bool TryHashToken(string? token, out byte[] tokenHash) {
    tokenHash = [];
    if (token is null) {
      return false;
    }

    Span<byte> raw = stackalloc byte[TOKEN_SIZE];
    if (!Convert.TryFromBase64String(token, raw, out var written) || written != TOKEN_SIZE) {
      return false;
    }

    tokenHash = SHA256.HashData(raw);
    return true;
  }

  #endregion

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
