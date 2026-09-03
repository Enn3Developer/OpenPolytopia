namespace OpenPolytopia.Server;

using OpenPolytopia.Common;

/// <summary>
/// Static gameplay data every game on the server is built from
/// </summary>
/// <remarks>
/// Loaded once by <see cref="GameServer"/> and shared by every <see cref="GameSession"/>; nothing here is
/// per-game state, unlike the <see cref="Grid"/>/<see cref="CityManager"/>/<see cref="TroopManager"/> a
/// <see cref="GameManager"/> builds for each game
/// </remarks>
public class GameData {
  /// <summary>
  /// The registered tribes
  /// </summary>
  public TribeManager Tribes { get; }

  /// <summary>
  /// The deserialized troop definitions, registered into a fresh <see cref="TroopManager"/> for every game
  /// </summary>
  public TroopsSerializedData TroopsSerializedData { get; }

  /// <summary>
  /// The registered building definitions
  /// </summary>
  public BuildingManager Buildings { get; }

  /// <summary>
  /// The shape of the tech tree every player's tree is built from
  /// </summary>
  public TechTreeDefinition TechTreeDefinition { get; }

  /// <summary>
  /// Builds a <see cref="GameData"/> from already loaded pieces
  /// </summary>
  /// <remarks>
  /// Mainly for tests, which build small fixtures instead of loading the full embedded resources
  /// </remarks>
  /// <param name="tribes">the registered tribes</param>
  /// <param name="troopsSerializedData">the deserialized troop definitions</param>
  /// <param name="buildings">the registered building definitions</param>
  /// <param name="techTreeDefinition">the shape of the tech tree every player's tree is built from</param>
  public GameData(TribeManager tribes, TroopsSerializedData troopsSerializedData, BuildingManager buildings,
    TechTreeDefinition techTreeDefinition) {
    Tribes = tribes;
    TroopsSerializedData = troopsSerializedData;
    Buildings = buildings;
    TechTreeDefinition = techTreeDefinition;
  }

  /// <summary>
  /// Loads every piece of gameplay data from the embedded json resources
  /// </summary>
  /// <returns>the loaded data</returns>
  /// <exception cref="InvalidOperationException">if one of the embedded json resources is empty or null</exception>
  public static GameData LoadEmbedded() {
    var tribes = new TribeManager();
    tribes.RegisterTribes(EmbeddedResources.LoadTribes() ??
      throw new InvalidOperationException("tribes.json is empty; can't load game data"));

    var troopsSerializedData = EmbeddedResources.LoadTroops() ??
      throw new InvalidOperationException("troops.json is empty; can't load game data");

    var buildings = new BuildingManager();
    buildings.RegisterBuildings(EmbeddedResources.LoadBuildings() ??
      throw new InvalidOperationException("buildings.json is empty; can't load game data"));

    var techTreeDefinition = TechTreeDefinition.FromSerializedData(EmbeddedResources.LoadTechTree() ??
      throw new InvalidOperationException("tech_tree.json is empty; can't load game data"));

    return new GameData(tribes, troopsSerializedData, buildings, techTreeDefinition);
  }
}
