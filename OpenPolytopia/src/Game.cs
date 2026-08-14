namespace OpenPolytopia;

using Godot;

public partial class Game : Control {
  [Export] public PackedScene? LobbyScene;

  private NetworkNode _network = null!;
  private string _playerName = "";
  private bool _switching;

  public override void _Ready() {
    // grab the instance once, the node could leave the tree before this scene
    _network = NetworkNode.Instance!;

    // switch to the lobby scene once the server accepts the player's name
    _network.OnNameSet += OnNameSet;
  }

  public override void _ExitTree() {
    base._ExitTree();
    _network.OnNameSet -= OnNameSet;
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

    _network.SetName(_playerName);
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
