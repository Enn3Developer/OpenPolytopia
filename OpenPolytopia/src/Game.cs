namespace OpenPolytopia;

using Godot;

public partial class Game : Control {
  [Export] public PackedScene? LobbyScene;

  public override void _Ready() {
    if (LobbyScene == null) {
      return;
    }

    if (!OS.HasFeature("dedicated_server")) {
      return;
    }

    var playerData = PlayerData.Instance;
    if (playerData.PlayerName == null) {
      var rng = new RandomNumberGenerator();
      playerData.PlayerName = $"Player{rng.Randi()}";
    }

    GetTree().ChangeSceneToPacked(LobbyScene);
  }

  private void OnNameChanged(string name) => PlayerData.Instance.PlayerName = name;
}
