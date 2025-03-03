namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Keep alive packet
/// </summary>
public class KeepAlivePacket : IPacket {
  public uint Captcha;

  public void Serialize(List<byte> bytes) => Captcha.Serialize(bytes);

  public void Deserialize(byte[] bytes) {
    var index = 0u;
    Captcha.Deserialize(bytes, ref index);
  }
}
