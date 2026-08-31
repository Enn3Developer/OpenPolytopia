namespace OpenPolytopia.Common.Network;

/// <summary>
/// Interface for types that need to be serialized to be sent on the network
/// </summary>
public interface INetworkSerializable {
  /// <summary>
  /// Serialize data
  /// </summary>
  /// <param name="bytes">the bytes where to serialize into, use <see cref="List{T}.Add"/></param>
  public void Serialize(List<byte> bytes);

  /// <summary>
  /// Deserialize data
  /// </summary>
  /// <param name="bytes">the buffer bytes where to read from</param>
  /// <param name="index">the index where to start reading</param>
  /// <remarks>
  /// It is assumed that every Deserialize operation increments <c>index</c> as needed.
  /// For example, <see langword="bool"/> increments <c>index</c> by one
  /// while <see langword="int"/> increments it by four
  /// </remarks>
  public void Deserialize(byte[] bytes, ref uint index);
}
