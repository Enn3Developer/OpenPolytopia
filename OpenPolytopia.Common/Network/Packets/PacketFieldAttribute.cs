namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Marks a field or property in a <see cref="GeneratedPacketAttribute"/> class for network serialization.
/// Members are serialized in source declaration order.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class PacketFieldAttribute : Attribute;
