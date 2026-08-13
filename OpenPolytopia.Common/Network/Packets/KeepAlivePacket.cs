namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Sent periodically by the server to check if a client is still alive
/// </summary>
/// <remarks>
/// The client must echo it back, otherwise the server closes the connection
/// </remarks>
public class KeepAlivePacket : IPacket {
  /// <summary>
  /// Random value that must be echoed back untouched
  /// </summary>
  public uint Captcha;

  public void Serialize(List<byte> bytes) => Captcha.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => Captcha.Deserialize(bytes, ref index);
}
