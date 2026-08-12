namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Sent periodically by the server; the client must echo it back.
/// If the server doesn't receive it back in time, it closes the connection.
/// </summary>
public class KeepAlivePacket : IPacket {
  /// <summary>
  /// Random value that must be echoed back untouched
  /// </summary>
  public uint Captcha;

  public void Serialize(List<byte> bytes) => Captcha.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Captcha.Deserialize(bytes, ref index);
}
