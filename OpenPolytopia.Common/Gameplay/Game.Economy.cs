namespace OpenPolytopia.Common.Gameplay;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Research, building/harvesting and city capture actions
/// </summary>
public partial class Game {
  /// <summary>
  /// Researches a tech node
  /// </summary>
  /// <param name="playerId">the player researching</param>
  /// <param name="techId">the id of the node to research</param>
  /// <returns>
  /// <see cref="GameActionResult.TechNotFound"/> if no node with that id exists in the player's tree,
  /// <see cref="GameActionResult.TechAlreadyResearched"/> if it's already researched,
  /// <see cref="GameActionResult.TechLocked"/> if its parent isn't researched yet, the usual turn/star checks, or
  /// <see cref="GameActionResult.Ok"/> on success
  /// </returns>
  public GameActionResult ResearchTech(int playerId, string techId) {
    var turnResult = CheckTurn(playerId, out var player);
    if (turnResult != GameActionResult.Ok) {
      return turnResult;
    }

    var node = player.TechTree.Find(techId);
    if (node == null) {
      return GameActionResult.TechNotFound;
    }

    if (node.Researched) {
      return GameActionResult.TechAlreadyResearched;
    }

    if (!player.TechTree.CanResearch(techId)) {
      return GameActionResult.TechLocked;
    }

    // Find already confirmed the node exists, so ComputeCost never returns null here
    var cost = player.TechTree.ComputeCost(techId, OwnedCities(playerId))!.Value;
    if (player.Stars < (int)cost) {
      return GameActionResult.NotEnoughStars;
    }

    player.Stars -= (int)cost;
    player.TechTree.Research(techId);
    player.Score.AddScore(ScoreType.DiscoveredNewTech);

    return GameActionResult.Ok;
  }

  /// <summary>
  /// Builds a building, a harvest action or a road on a tile
  /// </summary>
  /// <param name="playerId">the player building</param>
  /// <param name="position">the tile to build on</param>
  /// <param name="type">the type of building</param>
  /// <returns>the outcome of the request, see <see cref="BuildResult"/></returns>
  /// <remarks>
  /// <see cref="BuildingType.Road"/> is a special case: it doesn't need a city, an owner other than the acting
  /// player or nobody is fine, and it never touches population; every other building needs the tile to belong to
  /// a city owned by the player
  /// </remarks>
  public BuildResult Build(int playerId, Vector2I position, BuildingType type) {
    var turnResult = CheckTurn(playerId, out var player);
    if (turnResult != GameActionResult.Ok) {
      return RejectedBuild(turnResult);
    }

    if (!IsInside(position)) {
      return RejectedBuild(GameActionResult.InvalidPosition);
    }

    var building = Buildings[type];
    if (building == null) {
      return RejectedBuild(GameActionResult.InvalidBuilding);
    }

    return type == BuildingType.Road
      ? BuildRoad(player, position, building)
      : BuildStructure(player, position, type, building);
  }

  /// <summary>
  /// Captures a village or an enemy city
  /// </summary>
  /// <param name="playerId">the player capturing</param>
  /// <param name="position">the position of the city tile</param>
  /// <returns>the outcome of the request, see <see cref="CaptureResult"/></returns>
  public CaptureResult Capture(int playerId, Vector2I position) {
    var turnResult = CheckTurn(playerId, out var player);
    if (turnResult != GameActionResult.Ok) {
      return RejectedCapture(turnResult);
    }

    if (!IsInside(position)) {
      return RejectedCapture(GameActionResult.InvalidPosition);
    }

    var troop = Troops[position];
    if (!troop.IsValid()) {
      return RejectedCapture(GameActionResult.NoTroop);
    }

    if (troop.Player != (uint)playerId) {
      return RejectedCapture(GameActionResult.NotTroopOwner);
    }

    if (troop.Moved || troop.Attacked) {
      return RejectedCapture(GameActionResult.TroopAlreadyActed);
    }

    var tile = Grid[position];
    if (tile.Kind != TileKind.Village || tile.City == 0) {
      return RejectedCapture(GameActionResult.NotACaptureTarget);
    }

    var cityId = (uint)tile.City;
    var cityData = Cities[cityId];
    if (cityData.Owner == playerId) {
      return RejectedCapture(GameActionResult.NotACaptureTarget);
    }

    return ResolveCapture(player, position, cityId, cityData);
  }

  /// <summary>
  /// Builds a road on an open field/forest tile
  /// </summary>
  /// <param name="player">the player building</param>
  /// <param name="position">the tile to build on</param>
  /// <param name="road">the road's definition</param>
  /// <returns>the outcome of the request; <see cref="BuildResult.CityId"/> is always 0, roads have no city</returns>
  private BuildResult BuildRoad(PlayerState player, Vector2I position, Building road) {
    var tile = Grid[position];

    // a captured village always becomes Village kind, so requiring Field/Forest already excludes city tiles
    if ((tile.Kind != TileKind.Field && tile.Kind != TileKind.Forest) || tile.Roads) {
      return RejectedBuild(GameActionResult.WrongTile);
    }

    if (tile.Owner != player.Id && tile.Owner != 0) {
      return RejectedBuild(GameActionResult.TileNotOwned);
    }

    if (road.RequiredTech != null && !player.TechTree.HasResearched(road.RequiredTech)) {
      return RejectedBuild(GameActionResult.TechLocked);
    }

    if (player.Stars < (int)road.Cost) {
      return RejectedBuild(GameActionResult.NotEnoughStars);
    }

    player.Stars -= (int)road.Cost;
    Grid.ModifyTile(position, (ref Tile t) => t.Roads = true);

    return new BuildResult(GameActionResult.Ok, 0, 0, 0);
  }

  /// <summary>
  /// Builds a harvest action or a building on a tile that belongs to one of the player's cities
  /// </summary>
  /// <param name="player">the player building</param>
  /// <param name="position">the tile to build on</param>
  /// <param name="type">the building type, used to find what else is built adjacent to it</param>
  /// <param name="building">the building's definition</param>
  /// <returns>the outcome of the request</returns>
  private BuildResult BuildStructure(PlayerState player, Vector2I position, BuildingType type, Building building) {
    var tile = Grid[position];

    var kindMatches = tile.Kind == building.Kind || (building.Kind == TileKind.Water && tile.Kind == TileKind.Ocean);
    if (!kindMatches) {
      return RejectedBuild(GameActionResult.WrongTile);
    }

    if (tile.Owner != player.Id) {
      return RejectedBuild(GameActionResult.TileNotOwned);
    }

    if (tile.City == 0 || Cities[(uint)tile.City].Owner != player.Id) {
      return RejectedBuild(GameActionResult.TileNotInCity);
    }

    if (tile.Building != 0) {
      return RejectedBuild(GameActionResult.TileOccupied);
    }

    if (building.RequiresResource != ResourceType.None &&
        (!BuildingManager.TryGetResourceTile(building.RequiresResource, out _, out var resourceModifier) ||
        tile.Modifier != resourceModifier)) {
      return RejectedBuild(GameActionResult.MissingResource);
    }

    if (building.RequiredTech != null && !player.TechTree.HasResearched(building.RequiredTech)) {
      return RejectedBuild(GameActionResult.TechLocked);
    }

    var cityId = (uint)tile.City;
    if (building.UniquePerCity && CityHasBuilding(cityId, tile.Kind, building.TileBuilding)) {
      return RejectedBuild(GameActionResult.BuildingAlreadyInCity);
    }

    if (player.Stars < (int)building.Cost) {
      return RejectedBuild(GameActionResult.NotEnoughStars);
    }

    player.Stars -= (int)building.Cost;
    Grid.ModifyTile(position, (ref Tile t) => {
      if (building.TileBuilding != 0) {
        t.Building = building.TileBuilding;
      }

      if (building.ConsumesResource) {
        t.Modifier = 0;
      }
    });

    var adjacentCount = CountAdjacentMatching(position, building.AdjacentTo);
    var population = building.Population + (building.AdjacentPopulation * (uint)adjacentCount);
    var levelsGained = AddPopulation(cityId, population, player);

    // a building already standing next to this one may itself gain population from this new neighbor
    FeedAdjacentCities(position, type);

    if (building.IsTemple) {
      player.Score.AddScore(ScoreType.TemplesBuilt);
    }

    return new BuildResult(GameActionResult.Ok, cityId, population, levelsGained);
  }

  /// <summary>
  /// Resolves a legal capture: ownership flip, territory claim, scoring and elimination of the previous owner
  /// </summary>
  /// <param name="player">the capturing player</param>
  /// <param name="position">the city tile</param>
  /// <param name="cityId">the id of the captured city</param>
  /// <param name="cityData">the city's data before the capture</param>
  /// <returns>the resolved <see cref="CaptureResult"/></returns>
  private CaptureResult ResolveCapture(PlayerState player, Vector2I position, uint cityId, CityData cityData) {
    var previousOwnerId = cityData.Owner;
    var levelBeforeCapture = (uint)cityData.Level;
    var wasVillage = cityData.Level == 0;

    Cities.ModifyCity(cityId, (ref CityData city) => {
      city.Owner = player.Id;
      if (wasVillage) {
        city.Level = 1;
      }

      city.Troops = 0;
    });

    Grid.ModifyTile(position, (ref Tile t) => {
      t.Owner = player.Id;
      if (wasVillage) {
        t.Modifier = (int)VillageTileModifier.City;
      }
    });

    var claimedTiles = ClaimTerritory(position, cityId, player.Id);

    Troops.ModifyTroop(position, (ref TroopData data) => {
      data.Moved = true;
      data.Attacked = true;
    });

    player.Score.AddScore(ScoreType.VillageConquered);
    for (var claimed = 0u; claimed < claimedTiles; claimed++) {
      player.Score.AddScore(ScoreType.ClaimedTile);
    }

    var eliminatedPlayer = 0;
    if (previousOwnerId != 0 && this[previousOwnerId] is { } previousOwner) {
      previousOwner.Score.AddScore(ScoreType.LoseCity(levelBeforeCapture));

      if (OwnedCities(previousOwnerId) == 0) {
        // Eliminate checks game over on its own, so a capture that wipes out the last opponent ends the game here
        Eliminate(previousOwner);
        eliminatedPlayer = previousOwnerId;
      }
    }

    return new CaptureResult(GameActionResult.Ok, cityId, previousOwnerId, eliminatedPlayer, claimedTiles);
  }

  /// <summary>
  /// Claims the 3x3 territory around a just-captured city
  /// </summary>
  /// <param name="position">the city tile</param>
  /// <param name="cityId">the id of the captured city</param>
  /// <param name="playerId">the capturing player</param>
  /// <returns>how many tiles were newly claimed (previously without an owner or a city)</returns>
  private uint ClaimTerritory(Vector2I position, uint cityId, int playerId) {
    var claimedTiles = 0u;

    for (var dy = -1; dy <= 1; dy++) {
      for (var dx = -1; dx <= 1; dx++) {
        var neighbor = position + new Vector2I(dx, dy);
        if (!IsInside(neighbor)) {
          continue;
        }

        var neighborTile = Grid[neighbor];
        if (neighborTile.City == (int)cityId) {
          Grid.ModifyTile(neighbor, (ref Tile t) => t.Owner = playerId);
        }
        else if (neighborTile.Owner == 0 && neighborTile.City == 0 && neighborTile.Kind != TileKind.Village) {
          Grid.ModifyTile(neighbor, (ref Tile t) => {
            t.Owner = playerId;
            t.City = (int)cityId;
          });
          claimedTiles++;
        }
      }
    }

    return claimedTiles;
  }

  /// <summary>
  /// Adds population to a city, leveling it up as many times as the population allows
  /// </summary>
  /// <param name="cityId">the city gaining population</param>
  /// <param name="amount">the population to add</param>
  /// <param name="owner">the city's owner, scored for every level gained</param>
  /// <returns>how many levels the city gained</returns>
  private uint AddPopulation(uint cityId, uint amount, PlayerState owner) {
    if (amount == 0) {
      return 0;
    }

    var levelsGained = 0u;
    Cities.ModifyCity(cityId, (ref CityData city) => {
      city.Population += (int)amount;
      // TODO: level-up rewards (workshop, walls, parks...) aren't implemented yet
      while (city.LevelUp()) {
        levelsGained++;
      }
    });

    if (levelsGained > 0) {
      owner.Score.AddScore(ScoreType.CityLevelUp(levelsGained));
    }

    return levelsGained;
  }

  /// <summary>
  /// Counts the 8-neighbors of a tile whose Kind/Building match a building type
  /// </summary>
  /// <param name="position">the tile to look around</param>
  /// <param name="adjacentTo">the building type to count; nothing is counted when null or unregistered</param>
  /// <returns>how many neighbors have that building</returns>
  private int CountAdjacentMatching(Vector2I position, BuildingType? adjacentTo) {
    if (adjacentTo == null || Buildings[adjacentTo.Value] is not { } adjacentBuilding) {
      return 0;
    }

    var count = 0;
    foreach (var neighbor in Neighbors(position)) {
      var t = Grid[neighbor];
      if (t.Kind == adjacentBuilding.Kind && t.Building == adjacentBuilding.TileBuilding) {
        count++;
      }
    }

    return count;
  }

  /// <summary>
  /// Gives population to every neighboring city whose building grows next to a newly built one
  /// </summary>
  /// <param name="position">the tile that was just built on</param>
  /// <param name="builtType">the type of building just built there</param>
  private void FeedAdjacentCities(Vector2I position, BuildingType builtType) {
    foreach (var neighbor in Neighbors(position)) {
      var t = Grid[neighbor];
      if (t.Building == 0 || t.City == 0) {
        continue;
      }

      var neighborType = FindBuildingType(t.Kind, t.Building);
      if (neighborType == null || Buildings[neighborType.Value] is not { } neighborBuilding ||
          neighborBuilding.AdjacentTo != builtType) {
        continue;
      }

      var neighborCityId = (uint)t.City;
      if (this[Cities[neighborCityId].Owner] is { } neighborOwner) {
        AddPopulation(neighborCityId, neighborBuilding.AdjacentPopulation, neighborOwner);
      }
    }
  }

  /// <summary>
  /// Whether a city already has a tile with the given Kind/Building combination
  /// </summary>
  /// <param name="cityId">the city to scan</param>
  /// <param name="kind">the tile kind of the building</param>
  /// <param name="tileBuilding">the value stored in <see cref="Tile.Building"/> for that building</param>
  /// <returns>true if a tile of the city already carries that building</returns>
  private bool CityHasBuilding(uint cityId, TileKind kind, int tileBuilding) {
    for (var index = 0u; index < Grid.Size * Grid.Size; index++) {
      var t = Grid[index];
      if (t.City == (int)cityId && t.Kind == kind && t.Building == tileBuilding) {
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// Finds which registered building type produces a given Kind/Building combination
  /// </summary>
  /// <param name="kind">the tile kind</param>
  /// <param name="tileBuilding">the value stored in <see cref="Tile.Building"/></param>
  /// <returns>the building type; null if nothing registered matches (including plain empty tiles)</returns>
  private BuildingType? FindBuildingType(TileKind kind, int tileBuilding) {
    if (tileBuilding == 0) {
      return null;
    }

    foreach (var candidate in Enum.GetValues<BuildingType>()) {
      if (Buildings[candidate] is { } candidateBuilding && candidateBuilding.Kind == kind &&
          candidateBuilding.TileBuilding == tileBuilding) {
        return candidate;
      }
    }

    return null;
  }

  /// <summary>
  /// Every tile inside the grid, in the 8-neighborhood of a position
  /// </summary>
  /// <param name="position">the position to look around</param>
  /// <returns>the neighboring positions that are inside the grid</returns>
  private IEnumerable<Vector2I> Neighbors(Vector2I position) {
    for (var dy = -1; dy <= 1; dy++) {
      for (var dx = -1; dx <= 1; dx++) {
        if (dx == 0 && dy == 0) {
          continue;
        }

        var neighbor = position + new Vector2I(dx, dy);
        if (IsInside(neighbor)) {
          yield return neighbor;
        }
      }
    }
  }

  /// <summary>
  /// Builds the result for a build request rejected before doing anything
  /// </summary>
  /// <param name="result">the rejection reason</param>
  /// <returns>the rejected result, with every value zeroed out</returns>
  private static BuildResult RejectedBuild(GameActionResult result) => new(result, 0, 0, 0);

  /// <summary>
  /// Builds the result for a capture request rejected before doing anything
  /// </summary>
  /// <param name="result">the rejection reason</param>
  /// <returns>the rejected result, with every value zeroed out</returns>
  private static CaptureResult RejectedCapture(GameActionResult result) => new(result, 0, 0, 0, 0);
}

/// <summary>
/// The outcome of <see cref="Game.Build"/>
/// </summary>
/// <param name="Result">whether the request was accepted</param>
/// <param name="CityId">
/// the id of the city the built tile belongs to; 0 when the request was rejected or a road was built
/// </param>
/// <param name="Population">the population added to that city by this build</param>
/// <param name="LevelsGained">how many levels that city gained from the added population</param>
public readonly record struct BuildResult(GameActionResult Result, uint CityId, uint Population, uint LevelsGained);

/// <summary>
/// The outcome of <see cref="Game.Capture"/>
/// </summary>
/// <param name="Result">whether the request was accepted</param>
/// <param name="CityId">the id of the captured city; 0 when the request was rejected</param>
/// <param name="PreviousOwner">the id of the city's previous owner; 0 if it was an unowned village</param>
/// <param name="EliminatedPlayer">
/// the id of the previous owner if this capture eliminated them (they own no more cities); 0 otherwise
/// </param>
/// <param name="ClaimedTiles">how many previously ownerless tiles were newly claimed by the city's territory</param>
public readonly record struct CaptureResult(GameActionResult Result, uint CityId, int PreviousOwner,
  int EliminatedPlayer, uint ClaimedTiles);
