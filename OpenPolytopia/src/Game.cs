namespace OpenPolytopia;

using Godot;

public partial class Game : Control {
  [Export] public PackedScene? LobbyScene;

  public override void _Ready() {
    if (LobbyScene == null) {
      return;
    }

    if (OS.HasFeature("dedicated_server")) {
      GetTree().ChangeSceneToPacked(LobbyScene);
    }
  }
}
