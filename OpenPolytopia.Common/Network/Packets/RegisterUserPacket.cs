namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Register a new user on the server
/// </summary>
public class RegisterUserPacket : IPacket {
  public string Name = "";

  public void Serialize(List<byte> bytes) => Name.Serialize(bytes);

  public void Deserialize(byte[] bytes) {
    var index = 0u;
    Name = Name.Deserialize(bytes, ref index);
  }
}
