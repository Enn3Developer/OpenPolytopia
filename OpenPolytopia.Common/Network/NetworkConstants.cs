namespace OpenPolytopia.Common.Network;

public static class NetworkConstants {
  /// <summary>
  /// Protocol version; client and server must match to complete the handshake
  /// </summary>
  public const string VERSION = "0.1.0";

  /// <summary>
  /// Default port the server listens on
  /// </summary>
  public const int DEFAULT_PORT = 6969;

  /// <summary>
  /// Maximum allowed size in bytes of a single packet (id + payload)
  /// </summary>
  public const uint MAX_PACKET_SIZE = 1024 * 1024;
}
