namespace OpenPolytopia.Common;

using Network;
using Network.Packets;

public class PlayerData : INetworkSerializable {
  /// <summary>
  /// Name of the player
  /// </summary>
  public string PlayerName { get; set; } = "";

  public void Serialize(List<byte> bytes) => PlayerName.Serialize(bytes);

  public void Deserialize(byte[] bytes, ref uint index) => PlayerName = PlayerName.Deserialize(bytes, ref index);
}
