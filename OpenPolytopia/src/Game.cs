namespace OpenPolytopia;

using Godot;

public partial class Game : Control {
  [Export] public PackedScene? LobbyScene;

  public override void _Ready() {
    // Check if the lobby scene was set
    if (LobbyScene == null) {
      return;
    }

    GetTree().ChangeSceneToPacked(LobbyScene);
  }

  /// <summary>
  /// Sets the new name for the player
  /// </summary>
  /// <param name="name">the new player's name</param>
  private void OnNameChanged(string name) { }

  /// <summary>
  /// Waits until the player press the play button, creates a new random name if the player hasn't chosen one
  /// and connects him to the lobby
  /// </summary>
  private void OnPlayPressed() {
    GetTree().ChangeSceneToPacked(LobbyScene);
  }
}
