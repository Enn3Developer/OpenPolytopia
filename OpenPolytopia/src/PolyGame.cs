namespace OpenPolytopia;

using Godot;

public partial class PolyGame : Node3D {
  /// <summary>
  /// Report back to the server that we're ready to start
  /// </summary>
  public override void _Ready() {
    Lobby.Instance.RpcId(1, Lobby.MethodName.PlayerLoaded);
  }

  public override void _Process(double delta) {
  }

  /// <summary>
  /// Start the game
  /// </summary>
  /// <remarks>
  /// This is called only on the server
  /// </remarks>
  public void StartGame() {
  }
}
