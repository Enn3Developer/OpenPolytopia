namespace OpenPolytopia.Common.Network;

using Packets;

public static class PacketRegistrar {
  private static readonly Dictionary<uint, Func<IPacket>> _packetFactories = new(32);
  private static readonly Dictionary<Type, uint> _packetIds = new(32);
  private static readonly object _lock = new();
  private static bool _registered;

  /// <summary>
  /// Register a new packet with the given ID
  /// </summary>
  /// <param name="id">the id of the packet</param>
  /// <typeparam name="T">the type of the packet</typeparam>
  public static void RegisterPacket<T>(uint id) where T : IPacket, new() {
    _packetFactories.Add(id, () => new T());
    _packetIds.Add(typeof(T), id);
  }

  /// <summary>
  /// Creates a new empty packet instance given the ID
  /// </summary>
  /// <param name="id">the id of the packet</param>
  /// <returns>a new packet instance or null if the id isn't registered</returns>
  public static IPacket? CreatePacket(uint id) =>
    _packetFactories.TryGetValue(id, out var factory) ? factory() : null;

  /// <summary>
  /// Returns a packet ID given its type
  /// </summary>
  /// <param name="packet">the packet</param>
  /// <returns>the packet ID</returns>
  public static uint GetPacketId(IPacket packet) => _packetIds[packet.GetType()];

  /// <summary>
  /// Registers all the packets of the protocol
  /// </summary>
  /// <remarks>
  /// Calling this multiple times is safe
  /// </remarks>
  public static void RegisterAllPackets() {
    lock (_lock) {
      if (_registered) {
        return;
      }

      _registered = true;

      RegisterPacket<KeepAlivePacket>(0);
      RegisterPacket<HandshakePacket>(1);
      RegisterPacket<HandshakeResponsePacket>(2);
      RegisterPacket<SetNamePacket>(3);
      RegisterPacket<SetNameResponsePacket>(4);
      RegisterPacket<GetLobbiesPacket>(5);
      RegisterPacket<GetLobbiesResponsePacket>(6);
      RegisterPacket<CreateLobbyPacket>(7);
      RegisterPacket<CreateLobbyResponsePacket>(8);
      RegisterPacket<JoinLobbyPacket>(9);
      RegisterPacket<JoinLobbyResponsePacket>(10);
      RegisterPacket<LeaveLobbyPacket>(11);
      RegisterPacket<LeaveLobbyResponsePacket>(12);
      RegisterPacket<SetReadyPacket>(13);
      RegisterPacket<SetReadyResponsePacket>(14);
      RegisterPacket<LobbyUpdatedPacket>(15);
      RegisterPacket<LobbyDeletedPacket>(16);
      RegisterPacket<GameStartedPacket>(17);
      RegisterPacket<GetGameStatePacket>(18);
      RegisterPacket<GameStatePacket>(19);
      RegisterPacket<MoveTroopPacket>(20);
      RegisterPacket<MoveTroopResponsePacket>(21);
      RegisterPacket<TroopMovedPacket>(22);
      RegisterPacket<AttackPacket>(23);
      RegisterPacket<AttackResponsePacket>(24);
      RegisterPacket<CombatPacket>(25);
      RegisterPacket<TrainTroopPacket>(26);
      RegisterPacket<TrainTroopResponsePacket>(27);
      RegisterPacket<TroopTrainedPacket>(28);
      RegisterPacket<ResearchTechPacket>(29);
      RegisterPacket<ResearchTechResponsePacket>(30);
      RegisterPacket<TechResearchedPacket>(31);
      RegisterPacket<BuildPacket>(32);
      RegisterPacket<BuildResponsePacket>(33);
      RegisterPacket<BuildingBuiltPacket>(34);
      RegisterPacket<CapturePacket>(35);
      RegisterPacket<CaptureResponsePacket>(36);
      RegisterPacket<CityCapturedPacket>(37);
      RegisterPacket<EndTurnPacket>(38);
      RegisterPacket<EndTurnResponsePacket>(39);
      RegisterPacket<TurnStartedPacket>(40);
      RegisterPacket<PlayerEliminatedPacket>(41);
      RegisterPacket<GameOverPacket>(42);
    }
  }
}
