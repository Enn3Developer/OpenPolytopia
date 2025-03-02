namespace OpenPolytopia;

using Godot;

public partial class PlayerData : Node {
  public static PlayerData Instance => ((SceneTree)Engine.GetMainLoop()).Root.GetNode<PlayerData>("/root/PlayerData");

  public string? PlayerName { get; set; }
}
