namespace OpenPolytopia.Common.Network.Packets;

public class RegisterUserPacket : IPacket {
  public static RegisterUserPacket Default() => new() { Name = "" };

  public required string Name;

  public void Serialize(List<byte> bytes) => Name.Serialize(bytes);

  public void Deserialize(byte[] bytes) {
    var index = 0u;
    Name = Name.Deserialize(bytes, ref index);
  }
}
