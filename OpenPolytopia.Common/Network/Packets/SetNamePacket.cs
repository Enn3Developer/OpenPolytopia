namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Registers the player on the server or renames him
/// </summary>
[GeneratedPacket]
public partial class SetNamePacket : IPacket {
  /// <summary>
  /// The new name of the player
  /// </summary>
  [PacketField]
  public string Name = "";
}

/// <summary>
/// Server response to <see cref="SetNamePacket"/>
/// </summary>
[GeneratedPacket]
public partial class SetNameResponsePacket : IPacket {
  /// <summary>
  /// true if the name was accepted
  /// </summary>
  [PacketField]
  public bool Ok;
}
