namespace OpenPolytopia.Common.Network;

public interface INetworkSerializable {
  public void Serialize(List<byte> bytes);
  public void Deserialize(byte[] bytes, ref uint index);
}
