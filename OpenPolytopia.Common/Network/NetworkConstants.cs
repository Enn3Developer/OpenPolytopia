namespace OpenPolytopia.Common.Network;

public static class NetworkConstants {
  /// <summary>
  /// Version of the network protocol
  /// </summary>
  /// <remarks>
  /// The handshake fails if client and server have different versions
  /// </remarks>
  public const string VERSION = "0.2.0";

  /// <summary>
  /// Default port the server listens on
  /// </summary>
  public const int DEFAULT_PORT = 6969;

  /// <summary>
  /// Maximum size in bytes of a single packet, counting the packet id and the payload
  /// </summary>
  public const uint MAX_PACKET_SIZE = 1024 * 1024;
}
