namespace OpenPolytopia;

using Godot;

public partial class Game : Control {
  [Export] public PackedScene? LobbyScene;

  private string _playerName = "";
  private bool _switching;

  public override void _Ready() =>
    // switch to the lobby scene once the server accepts the player's name
    NetworkNode.Instance.OnNameSet += OnNameSet;

  public override void _ExitTree() {
    base._ExitTree();
    NetworkNode.Instance.OnNameSet -= OnNameSet;
  }

  /// <summary>
  /// Sets the new name for the player
  /// </summary>
  /// <param name="name">the new player's name</param>
  private void OnNameChanged(string name) => _playerName = name;

  /// <summary>
  /// Waits until the player press the play button, creates a new random name if the player hasn't chosen one
  /// and connects him to the lobby
  /// </summary>
  private void OnPlayPressed() {
    // create a random name if the player hasn't chosen one
    if (string.IsNullOrWhiteSpace(_playerName)) {
      _playerName = $"Player{GD.Randi() % 10000}";
    }

    NetworkNode.Instance.SetName(_playerName);
  }

  private void OnNameSet(bool ok) {
    if (!ok) {
      GD.PushError("The server refused the chosen name");
      return;
    }

    // Check if the lobby scene was set
    if (LobbyScene == null || _switching) {
      return;
    }

    _switching = true;
    GetTree().ChangeSceneToPacked(LobbyScene);
  }
}
