namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Registers the player on the server or renames him
/// </summary>
public class SetNamePacket : IPacket {
  /// <summary>
  /// The new name of the player
  /// </summary>
  public string Name = "";

  public void Serialize(List<byte> bytes) => Name.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Name = StringSerialization.Read(bytes, ref index);
}

/// <summary>
/// Server response to <see cref="SetNamePacket"/>
/// </summary>
public class SetNameResponsePacket : IPacket {
  /// <summary>
  /// true if the name was accepted
  /// </summary>
  public bool Ok;

  public void Serialize(List<byte> bytes) => Ok.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Ok.Deserialize(bytes, ref index);
}
