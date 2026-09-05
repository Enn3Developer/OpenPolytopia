namespace OpenPolytopia;

using Microsoft.Data.Sqlite;
using Server;
using Shouldly;

/// <summary>
/// Tests <see cref="ServerStore"/> against a real SQLite database in a temporary directory
/// </summary>
public class ServerStoreTest : IDisposable {
  private const string USERNAME = "Tester";
  private const string PASSWORD = "correct horse battery";

  /// <summary>
  /// A clock the tests can move forward at will
  /// </summary>
  private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider {
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
  }

  private readonly string _directory =
    Directory.CreateTempSubdirectory("openpolytopia-store-test-").FullName;

  private string DatabasePath => Path.Combine(_directory, "server.db");

  private ServerStore Open(TimeProvider? timeProvider = null) => new(DatabasePath, timeProvider);

  public void Dispose() {
    GC.SuppressFinalize(this);
    Directory.Delete(_directory, true);
  }

  #region Registration

  [Fact]
  public void RegisterReturnsAnAccountAndASession() {
    using var store = Open();

    var registration = store.Register(USERNAME, PASSWORD);

    registration.Account.Id.ShouldBe(1u);
    registration.Account.Username.ShouldBe("tester");
    registration.Account.DisplayName.ShouldBe(USERNAME);
    registration.Token.ShouldNotBeNullOrWhiteSpace();
    store.Resume(registration.Token).ShouldNotBeNull().Id.ShouldBe(registration.Account.Id);
  }

  [Fact]
  public void RegisterAssignsDistinctIds() {
    using var store = Open();

    var first = store.Register("first_user", PASSWORD);
    var second = store.Register("second_user", PASSWORD);

    second.Account.Id.ShouldNotBe(first.Account.Id);
  }

  [Fact]
  public void RegisterRejectsADuplicateNormalizedUsername() {
    using var store = Open();
    store.Register("Tester", PASSWORD);

    Should.Throw<ArgumentException>(() => store.Register("tEsTeR", PASSWORD));
  }

  [Fact]
  public void RegisterRollsBackTheAccountOfARejectedDuplicate() {
    using var store = Open();
    var original = store.Register("Tester", PASSWORD);

    Should.Throw<ArgumentException>(() => store.Register("TESTER", "a different password"));

    // the duplicate left nothing behind: the original credentials are still the only ones that work
    store.Login("tester", PASSWORD).ShouldNotBeNull().Account.Id.ShouldBe(original.Account.Id);
    store.Login("tester", "a different password").ShouldBeNull();
  }

  [Theory]
  [InlineData("")]
  [InlineData("ab")]
  [InlineData("this_username_is_way_too_long_to_be_accepted")]
  [InlineData("has space")]
  [InlineData("has-dash")]
  [InlineData("ünïcödé")]
  public void RegisterRejectsAnInvalidUsername(string username) {
    using var store = Open();

    Should.Throw<ArgumentException>(() => store.Register(username, PASSWORD));
  }

  [Theory]
  [InlineData("")]
  [InlineData("short")]
  [InlineData("elevenchars")]
  public void RegisterRejectsATooShortPassword(string password) {
    using var store = Open();

    Should.Throw<ArgumentException>(() => store.Register(USERNAME, password));
  }

  [Fact]
  public void RegisterRejectsATooLongPassword() {
    using var store = Open();

    Should.Throw<ArgumentException>(() => store.Register(USERNAME, new string('x', 129)));
  }

  [Fact]
  public void RegisterAcceptsThePasswordsAtTheBounds() {
    using var store = Open();

    store.Register("shortest", new string('x', 12)).ShouldNotBeNull();
    store.Register("longest", new string('x', 128)).ShouldNotBeNull();
  }

  #endregion

  #region Login

  [Fact]
  public void LoginAcceptsTheRightPasswordInAnyCasing() {
    using var store = Open();
    var registration = store.Register(USERNAME, PASSWORD);

    var login = store.Login("TESTER", PASSWORD).ShouldNotBeNull();

    login.Account.Id.ShouldBe(registration.Account.Id);
    login.Account.DisplayName.ShouldBe(USERNAME);
    login.Token.ShouldNotBe(registration.Token);
    store.Resume(login.Token).ShouldNotBeNull();
    // logging in doesn't invalidate the sessions that are already open
    store.Resume(registration.Token).ShouldNotBeNull();
  }

  [Fact]
  public void LoginRejectsAWrongPassword() {
    using var store = Open();
    store.Register(USERNAME, PASSWORD);

    store.Login(USERNAME, "wrong password!").ShouldBeNull();
  }

  [Fact]
  public void LoginRejectsAnUnknownAccount() {
    using var store = Open();
    store.Register(USERNAME, PASSWORD);

    store.Login("nobody", PASSWORD).ShouldBeNull();
  }

  [Theory]
  [InlineData("ab", PASSWORD)]
  [InlineData("has space", PASSWORD)]
  [InlineData(USERNAME, "short")]
  public void LoginRejectsMalformedCredentialsWithoutThrowing(string username, string password) {
    using var store = Open();
    store.Register(USERNAME, PASSWORD);

    store.Login(username, password).ShouldBeNull();
  }

  #endregion

  #region Sessions

  [Fact]
  public void ResumeRejectsAnUnknownOrMalformedToken() {
    using var store = Open();
    var registration = store.Register(USERNAME, PASSWORD);

    store.Resume("").ShouldBeNull();
    store.Resume("not base64 at all!").ShouldBeNull();
    store.Resume(Convert.ToBase64String(new byte[16])).ShouldBeNull();
    store.Resume(Convert.ToBase64String(new byte[32])).ShouldBeNull();
    // a token of the right shape but with a flipped character isn't the one that was handed out
    store.Resume(FlipFirstCharacter(registration.Token)).ShouldBeNull();
  }

  [Fact]
  public void LogoutRevokesTheSession() {
    using var store = Open();
    var registration = store.Register(USERNAME, PASSWORD);
    var other = store.Login(USERNAME, PASSWORD).ShouldNotBeNull();

    store.Logout(registration.Token).ShouldBeTrue();

    store.Resume(registration.Token).ShouldBeNull();
    // revoking one session leaves the other ones alone
    store.Resume(other.Token).ShouldNotBeNull();
    // revoking twice is a no-op
    store.Logout(registration.Token).ShouldBeFalse();
    store.Logout("garbage").ShouldBeFalse();
  }

  [Fact]
  public void ResumeRejectsAnExpiredSession() {
    var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
    using var store = Open(clock);
    var registration = store.Register(USERNAME, PASSWORD);

    clock.Advance(ServerStore.SessionLifetime - TimeSpan.FromMinutes(1));
    store.Resume(registration.Token).ShouldNotBeNull();

    clock.Advance(TimeSpan.FromMinutes(2));
    store.Resume(registration.Token).ShouldBeNull();
    // logging in again gives a session that's valid from now on
    var renewed = store.Login(USERNAME, PASSWORD).ShouldNotBeNull();
    store.Resume(renewed.Token).ShouldNotBeNull();
  }

  [Fact]
  public void AnExpiredSessionStaysRejectedAfterARestart() {
    var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
    string token;
    using (var store = Open(clock)) {
      token = store.Register(USERNAME, PASSWORD).Token;
    }

    clock.Advance(ServerStore.SessionLifetime + TimeSpan.FromDays(1));

    using var restarted = Open(clock);
    restarted.Resume(token).ShouldBeNull();
  }

  #endregion

  #region Persistence

  [Fact]
  public void AccountsAndSessionsSurviveARestart() {
    uint accountId;
    string token;
    using (var store = Open()) {
      var registration = store.Register(USERNAME, PASSWORD);
      accountId = registration.Account.Id;
      token = registration.Token;
    }

    using var restarted = Open();

    var resumed = restarted.Resume(token).ShouldNotBeNull();
    resumed.Id.ShouldBe(accountId);
    resumed.Username.ShouldBe("tester");
    resumed.DisplayName.ShouldBe(USERNAME);
    restarted.Login(USERNAME, PASSWORD).ShouldNotBeNull().Account.Id.ShouldBe(accountId);
    restarted.Login(USERNAME, "wrong password!").ShouldBeNull();
  }

  [Fact]
  public void AccountIdsAreNotReusedAcrossRestarts() {
    uint firstId;
    using (var store = Open()) {
      firstId = store.Register("first_user", PASSWORD).Account.Id;
    }

    using var restarted = Open();
    restarted.Register("second_user", PASSWORD).Account.Id.ShouldBeGreaterThan(firstId);
  }

  [Fact]
  public void StateIsEmptyUntilItsSaved() {
    using var store = Open();

    store.LoadState().ShouldBeNull();
  }

  [Fact]
  public void StateSurvivesARestart() {
    const string SNAPSHOT = """{"turn":12,"games":[]}""";
    using (var store = Open()) {
      store.SaveState(SNAPSHOT);
    }

    using var restarted = Open();
    restarted.LoadState().ShouldBe(SNAPSHOT);
  }

  [Fact]
  public void SaveStateReplacesThePreviousSnapshot() {
    using var store = Open();

    store.SaveState("""{"turn":1}""");
    store.SaveState("""{"turn":2}""");

    store.LoadState().ShouldBe("""{"turn":2}""");
  }

  #endregion

  #region Schema

  [Fact]
  public void OpeningRejectsANewerSchema() {
    using (var store = Open()) {
      store.Register(USERNAME, PASSWORD);
    }

    SetUserVersion(ServerStore.SCHEMA_VERSION + 1);

    Should.Throw<InvalidOperationException>(() => Open());
  }

  [Fact]
  public void OpeningAnAlreadyMigratedDatabaseKeepsItsData() {
    using (var store = Open()) {
      store.Register(USERNAME, PASSWORD);
      store.SaveState("""{"turn":1}""");
    }

    using (Open()) { }

    using var restarted = Open();
    restarted.Login(USERNAME, PASSWORD).ShouldNotBeNull();
    restarted.LoadState().ShouldBe("""{"turn":1}""");
  }

  [Fact]
  public void UsingADisposedStoreThrows() {
    var store = Open();
    store.Dispose();
    store.Dispose();

    Should.Throw<ObjectDisposedException>(() => store.LoadState());
  }

  #endregion

  private static string FlipFirstCharacter(string token) =>
    (token[0] == 'A' ? 'B' : 'A') + token[1..];

  private void SetUserVersion(int version) {
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
      DataSource = DatabasePath, Pooling = false
    }.ToString());
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA user_version = {version};";
    command.ExecuteNonQuery();
  }
}
