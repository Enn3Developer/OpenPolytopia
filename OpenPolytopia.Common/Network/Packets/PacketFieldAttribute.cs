namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Marks a field or property in a <see cref="GeneratedPacketAttribute"/> class for network serialization.
/// Members are serialized in source declaration order.
/// </summary>
/// <remarks>
/// Nullable members are prefixed with a boolean presence marker. When the marker is false, no value follows.
/// Nullable <see cref="INetworkSerializable"/> types must be concrete and have a public parameterless constructor
/// so the generated deserializer can create a present value.
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class PacketFieldAttribute : Attribute;
