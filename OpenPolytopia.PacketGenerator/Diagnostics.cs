namespace OpenPolytopia.PacketGenerator;

using Microsoft.CodeAnalysis;

internal static class Diagnostics {
  public static readonly DiagnosticDescriptor PacketOnly = new(
    "OPG001",
    "GeneratedPacket is only valid on packets",
    "Class '{0}' is marked with GeneratedPacket but does not implement IPacket",
    "PacketGenerator",
    DiagnosticSeverity.Error,
    true);

  public static readonly DiagnosticDescriptor PartialRequired = new(
    "OPG002",
    "Generated packets must be partial",
    "Packet '{0}' must be declared partial so its serialization methods can be generated",
    "PacketGenerator",
    DiagnosticSeverity.Error,
    true);

  public static readonly DiagnosticDescriptor UnsupportedType = new(
    "OPG003",
    "Packet field type cannot be serialized",
    "Packet member '{0}' has unsupported type '{1}'; use a type supported by NetworkSerialization or one that implements INetworkSerializable",
    "PacketGenerator",
    DiagnosticSeverity.Error,
    true);

  public static readonly DiagnosticDescriptor WritableRequired = new(
    "OPG004",
    "Packet field must be readable and writable",
    "Packet member '{0}' must be readable for serialization and writable for deserialization",
    "PacketGenerator",
    DiagnosticSeverity.Error,
    true);

  public static readonly DiagnosticDescriptor ManualImplementation = new(
    "OPG005",
    "Packet has manual serialization",
    "Packet '{0}' has PacketField members and manually implements {1}; that method will not be generated",
    "PacketGenerator",
    DiagnosticSeverity.Warning,
    true);

  public static readonly DiagnosticDescriptor SimpleClassRequired = new(
    "OPG006",
    "Packet shape is not supported",
    "Packet '{0}' must be a top-level, non-generic class to use PacketField",
    "PacketGenerator",
    DiagnosticSeverity.Error,
    true);

  public static readonly DiagnosticDescriptor GeneratedPacketRequired = new(
    "OPG007",
    "PacketField requires GeneratedPacket",
    "Packet member '{0}' is marked with PacketField, but class '{1}' is not marked with GeneratedPacket",
    "PacketGenerator",
    DiagnosticSeverity.Error,
    true);
}
