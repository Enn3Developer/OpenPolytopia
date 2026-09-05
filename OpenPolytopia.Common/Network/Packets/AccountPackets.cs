namespace OpenPolytopia.Common.Network.Packets;

/// <summary>Creates a persistent account with a unique username.</summary>
[GeneratedPacket]
public partial class RegisterAccountPacket : IPacket {
  [PacketField] public string Username = "";
  [PacketField] public string Password = "";
}

/// <summary>Authenticates an existing account.</summary>
[GeneratedPacket]
public partial class LoginPacket : IPacket {
  [PacketField] public string Username = "";
  [PacketField] public string Password = "";
}

/// <summary>Resumes an unexpired session after reconnecting.</summary>
[GeneratedPacket]
public partial class ResumeSessionPacket : IPacket {
  [PacketField] public string Token = "";
}

/// <summary>Revokes the current session and detaches from all game views.</summary>
[GeneratedPacket]
public partial class LogoutPacket : IPacket { }

/// <summary>Authentication result; PlayerId is the persistent account id.</summary>
[GeneratedPacket]
public partial class AuthenticationPacket : IPacket {
  [PacketField] public bool Ok;
  [PacketField] public uint PlayerId;
  [PacketField] public string Name = "";
  [PacketField] public string Token = "";
}

/// <summary>Lists games belonging to the authenticated account.</summary>
[GeneratedPacket]
public partial class GetMyGamesPacket : IPacket { }

/// <summary>Ids of the account's active and completed games.</summary>
[GeneratedPacket]
public partial class MyGamesPacket : IPacket {
  [PacketField] public ulong[] GameIds = [];
}

/// <summary>Opens an existing game membership and subscribes to updates.</summary>
[GeneratedPacket]
public partial class JoinGamePacket : IPacket {
  [PacketField] public ulong GameId;
}

/// <summary>Closes a game view without resigning.</summary>
[GeneratedPacket]
public partial class LeaveGamePacket : IPacket {
  [PacketField] public ulong GameId;
}

/// <summary>Explicitly resigns a member from a game.</summary>
[GeneratedPacket]
public partial class ResignGamePacket : IPacket {
  [PacketField] public ulong GameId;
}

/// <summary>Result of leaving or resigning from a game.</summary>
[GeneratedPacket]
public partial class MembershipResultPacket : IPacket {
  [PacketField] public ulong GameId;
  [PacketField] public Gameplay.GameActionResult Result;
}
