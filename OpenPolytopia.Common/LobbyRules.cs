namespace OpenPolytopia.Common;

/// <summary>
/// Rules a lobby must respect to be valid
/// </summary>
/// <remarks>
/// Lives in the common assembly so the client can check a lobby before asking the server for it
/// </remarks>
public static class LobbyRules {
  /// <summary>
  /// Smallest world a game can be played on
  /// </summary>
  public const uint MIN_WORLD_SIZE = 11;

  /// <summary>
  /// Biggest world a game can be played on
  /// </summary>
  public const uint MAX_WORLD_SIZE = 30;

  /// <summary>
  /// Minimum players needed to start a game; solo games aren't allowed
  /// </summary>
  public const uint MIN_PLAYERS = 2;

  /// <summary>
  /// World size from which a lobby can hold <see cref="MAX_PLAYERS_BIG_WORLD"/> players
  /// </summary>
  private const uint BIG_WORLD_SIZE = 14;

  /// <summary>
  /// Max players a world smaller than <see cref="BIG_WORLD_SIZE"/> can hold
  /// </summary>
  private const uint MAX_PLAYERS_SMALL_WORLD = 9;

  /// <summary>
  /// Max players a world of at least <see cref="BIG_WORLD_SIZE"/> can hold
  /// </summary>
  private const uint MAX_PLAYERS_BIG_WORLD = 16;

  /// <summary>
  /// Max players a lobby on a given world can hold
  /// </summary>
  /// <param name="worldSize">the size of the world</param>
  /// <returns>the max players allowed on that world</returns>
  public static uint MaxPlayersFor(uint worldSize) =>
    worldSize < BIG_WORLD_SIZE ? MAX_PLAYERS_SMALL_WORLD : MAX_PLAYERS_BIG_WORLD;

  /// <summary>
  /// Checks if a world can host a game
  /// </summary>
  /// <param name="worldSize">the size of the world</param>
  public static bool IsValidWorldSize(uint worldSize) => worldSize is >= MIN_WORLD_SIZE and <= MAX_WORLD_SIZE;

  /// <summary>
  /// Checks if a lobby of a given size can be created on a given world
  /// </summary>
  /// <param name="maxPlayers">max players that can join the lobby</param>
  /// <param name="worldSize">the size of the world</param>
  public static bool IsValidLobby(uint maxPlayers, uint worldSize) =>
    IsValidWorldSize(worldSize) && maxPlayers >= MIN_PLAYERS && maxPlayers <= MaxPlayersFor(worldSize);
}
