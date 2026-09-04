namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Generates network serialization for an <see cref="IPacket"/> from its <see cref="PacketFieldAttribute"/> members.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedPacketAttribute : Attribute;
