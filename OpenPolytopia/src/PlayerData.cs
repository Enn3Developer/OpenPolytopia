namespace OpenPolytopia;

using Godot;

/// <summary>
/// Class holding all the player data
/// </summary>
public partial class PlayerData : Node {
  /// <summary>
  /// Gets the instance of the current player's data
  /// </summary>
  public static PlayerData Instance => ((SceneTree)Engine.GetMainLoop()).Root.GetNode<PlayerData>("/root/PlayerData");

  /// <summary>
  /// Name of the player
  /// </summary>
  public string? PlayerName { get; set; }
}
