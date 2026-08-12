namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// First packet sent by the client to check protocol compatibility
/// </summary>
public class HandshakePacket : IPacket {
  /// <summary>
  /// Protocol version of the client, see <see cref="NetworkConstants.VERSION"/>
  /// </summary>
  public string Version = "";

  public void Serialize(List<byte> bytes) => Version.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Version = StringSerialization.Read(bytes, ref index);
}

/// <summary>
/// Server response to <see cref="HandshakePacket"/>
/// </summary>
public class HandshakeResponsePacket : IPacket {
  /// <summary>
  /// true if the client version is compatible with the server
  /// </summary>
  public bool Ok;

  /// <summary>
  /// Id assigned to the client by the server; valid only if <see cref="Ok"/> is true
  /// </summary>
  public uint PlayerId;

  public void Serialize(List<byte> bytes) {
    Ok.Serialize(bytes);
    PlayerId.Serialize(bytes);
  }

  public void Deserialize(byte[] bytes, ref uint index) {
    Ok.Deserialize(bytes, ref index);
    PlayerId.Deserialize(bytes, ref index);
  }
}
