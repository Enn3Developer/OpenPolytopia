namespace OpenPolytopia.Common.Network.Packets;

/// <summary>Authoritative UTC clock for the current turn; daily overdue clocks wait for a member to act.</summary>
[GeneratedPacket]
public partial class GameClockPacket : IPacket {
  [PacketField] public ulong GameId;
  [PacketField] public uint TimerMode;
  [PacketField] public uint PlayerId;
  [PacketField] public long DeadlineUnixMilliseconds;
  [PacketField] public long ServerUnixMilliseconds;
}

/// <summary>Skips or kicks the current player of an overdue daily game.</summary>
[GeneratedPacket]
public partial class ResolveOverdueTurnPacket : IPacket {
  [PacketField] public ulong GameId;
  [PacketField] public bool Kick;
  [PacketField] public uint ExpectedTurn;
  [PacketField] public uint ExpectedPlayer;
}
