namespace OpenPolytopia;

using Godot;

public partial class Game : Control {
  [Export] public PackedScene? LobbyScene;

  public override void _Ready() {
    // Check if the lobby scene was set
    if (LobbyScene == null) {
      return;
    }

    // Check if it is the server, if not just wait for the player to click play
    if (!OS.HasFeature("dedicated_server")) {
      return;
    }

    GetTree().ChangeSceneToPacked(LobbyScene);
  }

  /// <summary>
  /// Sets the new name for the player
  /// </summary>
  /// <param name="name">the new player's name</param>
  private void OnNameChanged(string name) => PlayerData.Instance.Data.PlayerName = name;

  /// <summary>
  /// Waits until the player press the play button, creates a new random name if the player hasn't chosen one
  /// and connects him to the lobby
  /// </summary>
  private void OnPlayPressed() {
    var playerData = PlayerData.Instance;
    // Generate player's name if missing
    if (playerData.Data.PlayerName.Length == 0) {
      var rng = new RandomNumberGenerator();
      playerData.Data.PlayerName = $"Player{rng.Randi()}";
    }

    GetTree().ChangeSceneToPacked(LobbyScene);
  }
}
