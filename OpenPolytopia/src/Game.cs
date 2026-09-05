namespace OpenPolytopia;

using Godot;

/// <summary>
/// Login screen; it logs the player into his account and moves on to the lobby scene
/// </summary>
/// <remarks>
/// A session saved by a previous run gets resumed automatically as soon as the client connects,
/// so this scene usually flashes by without the player typing anything
/// </remarks>
public partial class Game : Control {
  [Export] public PackedScene? LobbyScene;

  private NetworkNode _network = null!;
  private LineEdit _usernameEdit = null!;
  private LineEdit _passwordEdit = null!;
  private Button _loginButton = null!;
  private Button _registerButton = null!;
  private Label _statusLabel = null!;
  private bool _switching;

  public override void _Ready() {
    _usernameEdit = GetNode<LineEdit>("CenterContainer/VBoxContainer/UsernameEdit");
    _passwordEdit = GetNode<LineEdit>("CenterContainer/VBoxContainer/PasswordEdit");
    _loginButton = GetNode<Button>("CenterContainer/VBoxContainer/ButtonsContainer/LoginButton");
    _registerButton = GetNode<Button>("CenterContainer/VBoxContainer/ButtonsContainer/RegisterButton");
    _statusLabel = GetNode<Label>("CenterContainer/VBoxContainer/StatusLabel");

    _loginButton.Pressed += OnLoginPressed;
    _registerButton.Pressed += OnRegisterPressed;

    // grab the instance once, the node could leave the tree before this scene
    _network = NetworkNode.Instance!;

    // switch to the lobby scene once the server accepts the account
    _network.OnAuthenticated += OnAuthenticated;
    _network.OnConnected += OnNetworkConnected;
    _network.OnDisconnected += OnNetworkDisconnected;

    // the automatic resume could have logged this client in before this scene was ready
    if (_network.Authenticated) {
      OnAuthenticated(true);
    }
  }

  public override void _ExitTree() {
    base._ExitTree();
    _network.OnAuthenticated -= OnAuthenticated;
    _network.OnConnected -= OnNetworkConnected;
    _network.OnDisconnected -= OnNetworkDisconnected;
  }

  /// <summary>
  /// Logs into an existing account
  /// </summary>
  private void OnLoginPressed() {
    if (!ValidateCredentials(out var username, out var password)) {
      return;
    }

    _statusLabel.Text = "Logging in...";
    _network.Login(username, password);
  }

  /// <summary>
  /// Creates a new account and logs into it
  /// </summary>
  private void OnRegisterPressed() {
    if (!ValidateCredentials(out var username, out var password)) {
      return;
    }

    _statusLabel.Text = "Registering...";
    _network.Register(username, password);
  }

  /// <summary>
  /// Checks the typed credentials against the rules the server enforces, telling the player what is wrong with them
  /// </summary>
  /// <param name="username">the typed username</param>
  /// <param name="password">the typed password</param>
  /// <returns>true when both are valid</returns>
  private bool ValidateCredentials(out string username, out string password) {
    username = _usernameEdit.Text.Trim();
    password = _passwordEdit.Text;

    if (!NetworkNode.IsValidUsername(username)) {
      _statusLabel.Text =
        $"The username must be {NetworkNode.MIN_USERNAME_LENGTH} to {NetworkNode.MAX_USERNAME_LENGTH} " +
        "letters, digits and underscores";
      return false;
    }

    if (!NetworkNode.IsValidPassword(password)) {
      _statusLabel.Text =
        $"The password must be {NetworkNode.MIN_PASSWORD_LENGTH} to {NetworkNode.MAX_PASSWORD_LENGTH} characters";
      return false;
    }

    return true;
  }

  /// <summary>
  /// Clears the error shown while the server was unreachable
  /// </summary>
  private void OnNetworkConnected() => _statusLabel.Text = "";

  /// <summary>
  /// Tells the player the connection with the server got lost
  /// </summary>
  private void OnNetworkDisconnected() => _statusLabel.Text = "Disconnected from the server";

  private void OnAuthenticated(bool ok) {
    if (!ok) {
      // a refused resume lands here too: the player logs in again by hand
      _statusLabel.Text = "The server refused these credentials";
      return;
    }

    // the password is done with, don't keep it around in a widget
    _passwordEdit.Text = "";

    // Check if the lobby scene was set
    if (LobbyScene == null || _switching) {
      return;
    }

    _switching = true;
    GetTree().ChangeSceneToPacked(LobbyScene);
  }
}
