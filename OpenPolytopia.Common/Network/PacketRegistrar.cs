namespace OpenPolytopia.Common.Network;

using Packets;

public static class PacketRegistrar {
  private static readonly Dictionary<uint, Type> _packets = new(32);

  public static Type GetPacket(uint id) => _packets[id];

  public static void RegisterPacket<T>(uint id) where T : IPacket => _packets.Add(id, typeof(T));
}
