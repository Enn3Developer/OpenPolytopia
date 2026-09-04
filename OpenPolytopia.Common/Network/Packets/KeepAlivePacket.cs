namespace OpenPolytopia.Common.Network.Packets;

/// <summary>
/// Sent periodically by the server to check if a client is still alive
/// </summary>
/// <remarks>
/// The client echoes it back; a client that doesn't send anything for too long gets disconnected
/// </remarks>
[GeneratedPacket]
public partial class KeepAlivePacket : IPacket { }
