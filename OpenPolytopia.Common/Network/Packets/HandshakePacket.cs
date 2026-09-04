namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// First packet sent by the client to check protocol compatibility
/// </summary>
[GeneratedPacket]
public partial class HandshakePacket : IPacket {
  /// <summary>
  /// Protocol version of the client, see <see cref="NetworkConstants.VERSION"/>
  /// </summary>
  [PacketField]
  public string Version = "";
}

/// <summary>
/// Server response to <see cref="HandshakePacket"/>
/// </summary>
[GeneratedPacket]
public partial class HandshakeResponsePacket : IPacket {
  /// <summary>
  /// true if the client version is compatible with the server
  /// </summary>
  [PacketField]
  public bool Ok;

  /// <summary>
  /// Id assigned to the client by the server
  /// </summary>
  /// <remarks>
  /// Valid only if <see cref="Ok"/> is true
  /// </remarks>
  [PacketField]
  public uint PlayerId { get; set; }
}
