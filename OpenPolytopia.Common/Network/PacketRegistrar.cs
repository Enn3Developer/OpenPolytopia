namespace OpenPolytopia.Common.Network;

using Packets;

public static class PacketRegistrar {
  private static readonly Dictionary<uint, Type> _packets = new(32);
  private static readonly Dictionary<Type, uint> _packetIds = new(32);

  /// <summary>
  /// Register a new packet with the given ID
  /// </summary>
  /// <param name="id">the id of the packet</param>
  /// <typeparam name="T">the type of the packet</typeparam>
  public static void RegisterPacket<T>(uint id) where T : IPacket {
    _packets.Add(id, typeof(T));
    _packetIds.Add(typeof(T), id);
  }

  /// <summary>
  /// Returns a packet type given the ID
  /// </summary>
  /// <param name="id">the id of the packet</param>
  /// <returns>the packet type</returns>
  public static Type GetPacket(uint id) => _packets[id];

  /// <summary>
  /// Returns a packet ID given its type
  /// </summary>
  /// <param name="packet">the packet</param>
  /// <typeparam name="T">the packet type</typeparam>
  /// <returns>the packet ID</returns>
  public static uint GetPacketId<T>(T packet) where T : IPacket => _packetIds[typeof(T)];
}
