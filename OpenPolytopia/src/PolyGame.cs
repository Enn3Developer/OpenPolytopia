namespace OpenPolytopia;

using Godot;

public partial class PolyGame : Node3D {
  public override void _Ready() {
    Lobby.Instance.RpcId(1, Lobby.MethodName.PlayerLoaded);
  }

  public override void _Process(double delta) {
  }

  public void StartGame() {
  }
}
