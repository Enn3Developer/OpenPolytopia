namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Interface to declare a packet.
/// <br/>
/// A packet is sent on the wire as <c>[uint content length][uint packet id][payload]</c>
/// where the content length covers the packet id and the payload.
/// Every packet type must be registered in <see cref="PacketRegistrar"/> with a unique id
/// and must have a parameterless constructor.
/// </summary>
public interface IPacket : INetworkSerializable;
