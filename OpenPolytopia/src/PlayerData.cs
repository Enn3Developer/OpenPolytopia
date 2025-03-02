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
  /// Internal data to use to read/modify the player's data
  /// </summary>
  public Common.PlayerData Data { get; } = new();
}
