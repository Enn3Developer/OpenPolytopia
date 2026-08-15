namespace OpenPolytopia.Common;

using System.Runtime.CompilerServices;

/// <summary>
/// Terrain generation system
/// </summary>
/// <remarks>
/// Implements the Polytopia map generation pipeline:
/// <list type="number">
/// <item>Land generation: random land seeding followed by cellular-automata smoothing passes</item>
/// <item>Capital placement: capitals are placed on land maximizing the distance between each other</item>
/// <item>Terrain assignment: every tile gets the biome of the nearest capital and rolls
/// mountain/forest/water using the tribe terrain rates</item>
/// <item>Village placement: villages are placed on free land at least 2 tiles away from other villages</item>
/// <item>Resource spawning: resources spawn only within 2 tiles of a city or village,
/// with reduced rates on the outer ring</item>
/// <item>Ruins: <c>tiles / 40</c> ancient ruins, at most a third of them on water</item>
/// </list>
/// </remarks>
/// <param name="grid">the grid to use</param>
/// <param name="cityManager">the city manager to use</param>
/// <param name="tribeManager">the tribe manager to use</param>
/// <param name="players">all the players in the game</param>
/// <param name="seed">the optional random seed</param>
public class TerrainGeneration(
  Grid grid,
  CityManager cityManager,
  TribeManager tribeManager,
  Player[] players,
  int? seed = null) {
  // base terrain rates, multiplied by every tribe's TerrainRate
  private const float BASE_FOREST_RATE = 0.38f;
  private const float BASE_MOUNTAIN_RATE = 0.14f;

  // base resource rates, multiplied by every tribe's SpawnRate
  private const float BASE_FRUIT_RATE = 0.18f;
  private const float BASE_CROP_RATE = 0.18f;
  private const float BASE_ANIMAL_RATE = 0.19f;
  private const float BASE_FISH_RATE = 0.5f;
  private const float BASE_MINERAL_RATE = 0.11f;

  // stars have no rate in SpawnRate, so they only use the base rate
  private const float BASE_STAR_RATE = 0.4f;

  // resources on the border expansion ring (2 tiles away from a city) spawn at a third of the rate
  private const float BORDER_EXPANSION_RATE = 1f / 3f;

  // capitals can't spawn closer than this to the map border
  private const int CAPITAL_EDGE_MARGIN = 2;

  // one ruin every RUINS_DIVISOR tiles, at most a third of them on water
  private const int RUINS_DIVISOR = 40;

  // Tile.City is 8 bits so there can't be more than 255 cities in a grid
  private const int MAX_CITIES = 255;

  // zone map values, used for village spacing and resource spawning
  private const byte ZONE_FREE = 0;
  private const byte ZONE_BORDER = 1;
  private const byte ZONE_TERRITORY = 2;
  private const byte ZONE_CITY = 3;

  // create the random number generator from a seed if set
  private readonly Random _rng = seed == null ? new Random() : new Random(seed.Value);

  // distance-to-city zones; rebuilt while placing capitals and villages
  private readonly byte[] _zoneMap = new byte[grid.Size * grid.Size];

  // grid indexes of the capitals, aligned with players
  private readonly List<uint> _capitals = new(players.Length);

  private int _citiesCount;
  private bool _generated;

  /// <summary>
  /// Fraction of the map converted to land before the smoothing passes
  /// </summary>
  public float InitialLand { get; init; } = 0.5f;

  /// <summary>
  /// Number of cellular-automata smoothing passes applied to the initial land
  /// </summary>
  public int Smoothing { get; init; } = 3;

  /// <summary>
  /// Land/water balance of the smoothing passes
  /// </summary>
  /// <remarks>
  /// A cell stays land when at most <c>Relief</c> of the 9 cells around it are water,
  /// so lower values produce more compact continents while higher values produce rougher coasts
  /// </remarks>
  public int Relief { get; init; } = 4;

  /// <summary>
  /// Base probability of a land tile converting to water, multiplied by the tribe's
  /// <see cref="TerrainRate.WaterRate"/>
  /// </summary>
  public float WaterRate { get; init; } = 0.05f;

  /// <summary>
  /// Generates the map ready to use in-game
  /// </summary>
  /// <remarks>
  /// An instance can only generate one map because the grid and the city manager keep the
  /// generated data; create a new <see cref="TerrainGeneration"/> to generate another map
  /// </remarks>
  /// <exception cref="InvalidOperationException">
  /// if the map has already been generated or the players are invalid: there must be
  /// between 1 and 15 players, every player id must be unique and between 1 and 15 and
  /// every tribe must be a defined <see cref="TribeType"/>
  /// </exception>
  /// <example>
  /// <code>
  /// var terrainGeneration = new TerrainGeneration(grid, cityManager, tribeManager, players);
  /// await terrainGeneration.GenerateMapAsync();
  /// </code>
  /// </example>
  public async Task GenerateMapAsync() {
    // the grid and the city manager keep the generated data, so an instance is single-use
    if (_generated) {
      throw new InvalidOperationException(
        "the map has already been generated; create a new TerrainGeneration to generate another map");
    }

    // Tile.Owner is 4 bits, so there can't be more than 15 players
    if (players.Length is 0 or > 15) {
      throw new InvalidOperationException(
        $"invalid number of players: {players.Length}; must be between 1 and 15");
    }

    var seenIds = 0;
    foreach (var player in players) {
      // player ids must fit in the 4-bit Tile.Owner field, where 0 means no owner
      if (player.Id is < 1 or > 15) {
        throw new InvalidOperationException($"invalid player id: {player.Id}; must be between 1 and 15");
      }

      // a duplicate id would merge the territories of two players
      if ((seenIds & (1 << player.Id)) != 0) {
        throw new InvalidOperationException($"duplicate player id: {player.Id}");
      }

      seenIds |= 1 << player.Id;

      // an undefined tribe would silently fall back to the base rates
      if (!Enum.IsDefined(player.Tribe)) {
        throw new InvalidOperationException($"invalid tribe: {player.Tribe}");
      }
    }

    _generated = true;

    await GenerateLandAsync();
    await GenerateInitialCitiesAsync();
    await GenerateTerrainAsync();
    await GenerateCitiesAsync();
    await GenerateResourcesAsync();
    await GenerateRuinsAsync();
  }

  /// <summary>
  /// Generates the land/ocean layout
  /// </summary>
  /// <remarks>
  /// Starts from a full ocean map, converts random tiles to land until <see cref="InitialLand"/>
  /// is reached, then applies <see cref="Smoothing"/> cellular-automata passes where a tile stays
  /// land when at most <see cref="Relief"/> of the 9 cells around it are water
  /// </remarks>
  private async Task GenerateLandAsync() {
    var size = (int)grid.Size;
    var cells = size * size;

    for (var i = 0u; i < cells; i++) {
      grid.ModifyTile(i, (ref Tile tile) => tile.Kind = TileKind.Ocean);
    }

    // seed random land tiles until the initial land fraction is reached
    var target = (int)(cells * Math.Clamp(InitialLand, 0f, 1f));
    var landCount = 0;
    while (landCount < target) {
      var index = (uint)_rng.Next(0, cells);
      if (grid[index].Kind != TileKind.Ocean) {
        continue;
      }

      grid.ModifyTile(index, (ref Tile tile) => tile.Kind = TileKind.Field);
      landCount++;
    }

    // smooth the noise into continuous landmasses
    var relief = Math.Clamp(Relief, 0, 8);
    var snapshot = new TileKind[cells];
    for (var pass = 0; pass < Smoothing; pass++) {
      // yield to the task executor
      await Task.Yield();

      for (var i = 0u; i < cells; i++) {
        snapshot[i] = grid[i].Kind;
      }

      for (var y = 0; y < size; y++) {
        for (var x = 0; x < size; x++) {
          // count water cells in the 3x3 neighborhood; out-of-bounds cells count as water
          var waterCount = 0;
          for (var dy = -1; dy <= 1; dy++) {
            for (var dx = -1; dx <= 1; dx++) {
              var nx = x + dx;
              var ny = y + dy;
              if (nx < 0 || ny < 0 || nx >= size || ny >= size ||
                  snapshot[(ny * size) + nx] == TileKind.Ocean) {
                waterCount++;
              }
            }
          }

          // the indexer avoids allocating a closure capturing the computed kind for every tile
          var index = grid.GridPositionToIndex(x, y);
          var tile = grid[index];
          tile.Kind = waterCount <= relief ? TileKind.Field : TileKind.Ocean;
          grid[index] = tile;
        }
      }
    }
  }

  /// <summary>
  /// Places a capital for every player
  /// </summary>
  /// <remarks>
  /// Capitals only spawn on land at least <see cref="CAPITAL_EDGE_MARGIN"/> tiles away from the
  /// map border; every capital is placed on the tile maximizing the minimum distance to the
  /// already placed capitals so players don't start too close to one another
  /// </remarks>
  private async Task GenerateInitialCitiesAsync() {
    foreach (var player in players) {
      // yield to the task executor
      await Task.Yield();

      var candidates = CollectCapitalCandidates();

      // keep the tiles with the maximum distance to the closest already placed capital
      var best = new List<uint>();
      var bestScore = -1;
      foreach (var candidate in candidates) {
        var score = int.MaxValue;
        foreach (var capital in _capitals) {
          score = Math.Min(score, Distance(candidate, capital));
        }

        if (score > bestScore) {
          bestScore = score;
          best.Clear();
          best.Add(candidate);
        }
        else if (score == bestScore) {
          best.Add(candidate);
        }
      }

      var index = best[_rng.Next(best.Count)];
      _capitals.Add(index);
      _citiesCount++;

      // register the city
      var cityId = cityManager.RegisterCity(index);

      // set all data for the tile in the grid
      grid.ModifyTile(index, (ref Tile tile) => {
        tile.Owner = player.Id;
        tile.Kind = TileKind.Village;
        tile.Modifier = (int)VillageTileModifier.City;
        tile.Biome = player.Tribe;
      });

      // set all data for the city
      cityManager.ModifyCity(cityId, (ref CityData city) => {
        city.Capital = true;
        city.Owner = player.Id;
        city.Level = 1;
      });

      // claim the starting 3x3 territory; never steal tiles already claimed by another capital
      ForEachInRadius(index, 1, neighbor => grid.ModifyTile(neighbor, (ref Tile tile) => {
        if (tile.Owner != 0 || tile.Kind == TileKind.Village) {
          return;
        }

        tile.Owner = player.Id;
        tile.City = (int)cityId;
      }));

      MarkCityZone(index);
    }
  }

  /// <summary>
  /// Collects the tiles where a capital can spawn
  /// </summary>
  /// <remarks>
  /// Starts with free land at least <see cref="CAPITAL_EDGE_MARGIN"/> tiles away from the map
  /// border and progressively relaxes the requirements so capitals can always be placed, even
  /// on tiny or very watery maps
  /// </remarks>
  /// <returns>the indexes of the valid spawn tiles</returns>
  private List<uint> CollectCapitalCandidates() {
    var cells = grid.Size * grid.Size;
    var candidates = new List<uint>();

    (int margin, byte maxZone)[] tiers = [(CAPITAL_EDGE_MARGIN, ZONE_FREE), (1, ZONE_FREE), (0, ZONE_BORDER)];
    foreach (var (margin, maxZone) in tiers) {
      for (var i = 0u; i < cells; i++) {
        grid.IndexToGridPosition(i, out var x, out var y);
        if (grid[i].Kind == TileKind.Field && grid[i].Owner == 0 && _zoneMap[i] <= maxZone &&
            x >= margin && y >= margin && x < grid.Size - margin && y < grid.Size - margin) {
          candidates.Add(i);
        }
      }

      if (candidates.Count > 0) {
        return candidates;
      }
    }

    // last resort: raise a random unclaimed tile from the ocean, preferring free zones
    var free = new List<uint>();
    var unclaimed = new List<uint>();
    for (var i = 0u; i < cells; i++) {
      if (grid[i].Kind == TileKind.Village || grid[i].Owner != 0) {
        continue;
      }

      unclaimed.Add(i);
      if (_zoneMap[i] == ZONE_FREE) {
        free.Add(i);
      }
    }

    var pool = free.Count > 0 ? free : unclaimed;
    if (pool.Count == 0) {
      throw new InvalidOperationException("not enough tiles to place all the capitals");
    }

    var index = pool[_rng.Next(pool.Count)];
    grid.ModifyTile(index, (ref Tile tile) => tile.Kind = TileKind.Field);
    candidates.Add(index);
    return candidates;
  }

  /// <summary>
  /// Assigns biomes and terrain types
  /// </summary>
  /// <remarks>
  /// Every tile gets the biome of the nearest capital; every land tile then rolls water,
  /// mountain and forest in this order using the biome tribe's <see cref="TerrainRate"/>,
  /// defaulting to field.
  /// <br/>
  /// Finally, ocean tiles adjacent to land become shallow water
  /// </remarks>
  private async Task GenerateTerrainAsync() {
    var size = (int)grid.Size;
    var cells = (uint)(size * size);

    for (var i = 0u; i < cells; i++) {
      if (i % grid.Size == 0) {
        // yield to the task executor once per row
        await Task.Yield();
      }

      // the indexer avoids allocating a closure capturing the biome for every tile
      var biome = NearestCapitalTribe(i);
      var biomeTile = grid[i];
      biomeTile.Biome = biome;
      grid[i] = biomeTile;

      if (grid[i].Kind != TileKind.Field) {
        continue;
      }

      var rates = tribeManager[biome]?.TerrainRate;
      var waterRate = WaterRate * (rates?.WaterRate ?? 1f);
      var mountainRate = BASE_MOUNTAIN_RATE * (rates?.MountainRate ?? 1f);
      var forestRate = BASE_FOREST_RATE * (rates?.ForestRate ?? 1f);

      // city territory never converts to water, otherwise a capital could end up without land
      if (grid[i].Owner == 0 && _rng.NextSingle() < waterRate) {
        grid.ModifyTile(i, (ref Tile tile) => tile.Kind = TileKind.Water);
        continue;
      }

      // mountain first, then forest; fields are just the remainder
      var roll = _rng.NextSingle();
      if (roll < mountainRate) {
        grid.ModifyTile(i, (ref Tile tile) => tile.Kind = TileKind.Mountain);
      }
      else if (roll < mountainRate + forestRate) {
        grid.ModifyTile(i, (ref Tile tile) => tile.Kind = TileKind.Forest);
      }
    }

    // ocean adjacent to land becomes shallow water
    var snapshot = new TileKind[cells];
    for (var i = 0u; i < cells; i++) {
      snapshot[i] = grid[i].Kind;
    }

    for (var y = 0; y < size; y++) {
      for (var x = 0; x < size; x++) {
        if (snapshot[(y * size) + x] != TileKind.Ocean) {
          continue;
        }

        var nearLand = (x > 0 && IsLand(snapshot[(y * size) + x - 1])) ||
                       (x < size - 1 && IsLand(snapshot[(y * size) + x + 1])) ||
                       (y > 0 && IsLand(snapshot[((y - 1) * size) + x])) ||
                       (y < size - 1 && IsLand(snapshot[((y + 1) * size) + x]));
        if (nearLand) {
          var index = grid.GridPositionToIndex(x, y);
          var tile = grid[index];
          tile.Kind = TileKind.Water;
          grid[index] = tile;
        }
      }
    }
  }

  /// <summary>
  /// Places the villages
  /// </summary>
  /// <remarks>
  /// Villages spawn on free field/forest tiles at least 1 tile away from the map border and
  /// 2 tiles away from any other city or village, until no free tile remains
  /// </remarks>
  private async Task GenerateCitiesAsync() {
    var size = (int)grid.Size;
    var cells = (uint)(size * size);

    // collect all the tiles where a village can spawn; border expansion tiles are allowed
    // so villages can be exactly 2 tiles away from another city
    var candidates = new List<uint>();
    for (var i = 0u; i < cells; i++) {
      grid.IndexToGridPosition(i, out var x, out var y);
      if (_zoneMap[i] <= ZONE_BORDER && grid[i].Kind is TileKind.Field or TileKind.Forest &&
          x > 0 && y > 0 && x < size - 1 && y < size - 1) {
        candidates.Add(i);
      }
    }

    while (candidates.Count > 0 && _citiesCount < MAX_CITIES) {
      var position = _rng.Next(candidates.Count);
      var index = candidates[position];
      candidates.RemoveAt(position);

      // a previously placed village may have claimed this tile as its territory
      if (_zoneMap[index] > ZONE_BORDER) {
        continue;
      }

      // yield to the task executor once per placed village
      await Task.Yield();

      // register the village; the default CityData already means "village" (no owner, level 0)
      cityManager.RegisterCity(index);
      _citiesCount++;

      grid.ModifyTile(index, (ref Tile tile) => {
        tile.Kind = TileKind.Village;
        tile.Modifier = (int)VillageTileModifier.Village;
      });

      MarkCityZone(index);
    }
  }

  /// <summary>
  /// Spawns the resources
  /// </summary>
  /// <remarks>
  /// Resources only spawn within 2 tiles of a city or village, at full rate inside city
  /// territory and at a third of the rate on the outer ring, using the biome tribe's
  /// <see cref="SpawnRate"/> as multiplier
  /// </remarks>
  private async Task GenerateResourcesAsync() {
    var cells = grid.Size * grid.Size;

    for (var i = 0u; i < cells; i++) {
      if (i % grid.Size == 0) {
        // yield to the task executor once per row
        await Task.Yield();
      }

      var zone = _zoneMap[i];
      if (zone is not ZONE_TERRITORY and not ZONE_BORDER) {
        continue;
      }

      var multiplier = zone == ZONE_TERRITORY ? 1f : BORDER_EXPANSION_RATE;
      var tile = grid[i];
      var rates = tribeManager[tile.Biome]?.SpawnRate;

      switch (tile.Kind) {
        case TileKind.Field: {
          // fruit first, then crop; both can't spawn on the same tile
          var fruitRate = BASE_FRUIT_RATE * (rates?.FruitRate ?? 1f) * multiplier;
          var cropRate = BASE_CROP_RATE * (rates?.CropRate ?? 1f) * multiplier;
          var roll = _rng.NextSingle();
          if (roll < fruitRate) {
            grid.ModifyTile(i, (ref Tile t) => t.SetTileModifier(FieldTileModifier.Fruit));
          }
          else if (roll < fruitRate + cropRate) {
            grid.ModifyTile(i, (ref Tile t) => t.SetTileModifier(FieldTileModifier.Crop));
          }

          break;
        }
        case TileKind.Forest:
          if (_rng.NextSingle() < BASE_ANIMAL_RATE * (rates?.AnimalRate ?? 1f) * multiplier) {
            grid.ModifyTile(i, (ref Tile t) => t.SetTileModifier(ForestTileModifier.Animal));
          }

          break;
        case TileKind.Mountain:
          if (_rng.NextSingle() < BASE_MINERAL_RATE * (rates?.MineralRate ?? 1f) * multiplier) {
            grid.ModifyTile(i, (ref Tile t) => t.SetTileModifier(MountainTileModifier.Ore));
          }

          break;
        case TileKind.Water:
          if (_rng.NextSingle() < BASE_FISH_RATE * (rates?.FishRate ?? 1f) * multiplier) {
            grid.ModifyTile(i, (ref Tile t) => t.SetTileModifier(WaterTileModifier.Fish));
          }

          break;
        case TileKind.Ocean:
          if (_rng.NextSingle() < BASE_STAR_RATE * multiplier) {
            grid.ModifyTile(i, (ref Tile t) => t.SetTileModifier(OceanTileModifier.Star));
          }

          break;
        case TileKind.Village:
        default:
          break;
      }
    }
  }

  /// <summary>
  /// Places the ancient ruins
  /// </summary>
  /// <remarks>
  /// Spawns <c>tiles / 40</c> ruins away from cities and villages, never adjacent to another
  /// ruin and with at most a third of them on water
  /// </remarks>
  private async Task GenerateRuinsAsync() {
    // yield to the task executor
    await Task.Yield();

    var cells = (int)(grid.Size * grid.Size);
    var total = cells / RUINS_DIVISOR;
    var maxOnWater = total / 3;

    var placed = 0;
    var placedOnWater = 0;
    // random placement can fail, so bound the number of attempts
    var attempts = total * 30;
    while (placed < total && attempts-- > 0) {
      var index = (uint)_rng.Next(0, cells);
      var tile = grid[index];

      // keep ruins away from cities, villages, their territory and tiles with a resource
      if (tile.Ruin || tile.Modifier != 0 || tile.Kind == TileKind.Village || _zoneMap[index] >= ZONE_TERRITORY) {
        continue;
      }

      var onWater = tile.Kind is TileKind.Water or TileKind.Ocean;
      if (onWater && placedOnWater >= maxOnWater) {
        continue;
      }

      // never place two ruins next to each other
      var nearRuin = false;
      ForEachInRadius(index, 1, neighbor => nearRuin |= grid[neighbor].Ruin);
      if (nearRuin) {
        continue;
      }

      grid.ModifyTile(index, (ref Tile t) => t.Ruin = true);
      placed++;
      if (onWater) {
        placedOnWater++;
      }
    }
  }

  /// <summary>
  /// Marks the zones around a new city or village: the tile itself as city, the tiles within
  /// 1 tile as territory and the tiles within 2 tiles as border expansion
  /// </summary>
  /// <param name="index">index of the city tile</param>
  private void MarkCityZone(uint index) {
    _zoneMap[index] = ZONE_CITY;
    ForEachInRadius(index, 2,
      neighbor => _zoneMap[neighbor] = Math.Max(_zoneMap[neighbor], ZONE_BORDER));
    ForEachInRadius(index, 1,
      neighbor => _zoneMap[neighbor] = Math.Max(_zoneMap[neighbor], ZONE_TERRITORY));
  }

  /// <summary>
  /// Returns the tribe of the capital closest to a tile
  /// </summary>
  /// <param name="index">index of the tile</param>
  /// <returns>the tribe of the nearest capital, or the first player's tribe if there are no capitals</returns>
  private TribeType NearestCapitalTribe(uint index) {
    var tribe = players[0].Tribe;
    var bestDistance = int.MaxValue;
    for (var i = 0; i < _capitals.Count; i++) {
      var distance = Distance(index, _capitals[i]);
      if (distance < bestDistance) {
        bestDistance = distance;
        tribe = players[i].Tribe;
      }
    }

    return tribe;
  }

  /// <summary>
  /// Runs a callback for every tile within a radius, excluding the center tile itself
  /// </summary>
  /// <param name="index">index of the center tile</param>
  /// <param name="radius">the radius</param>
  /// <param name="action">callback run with the index of every neighbor</param>
  private void ForEachInRadius(uint index, int radius, Action<uint> action) {
    var size = (int)grid.Size;
    grid.IndexToGridPosition(index, out var centerX, out var centerY);
    for (var dy = -radius; dy <= radius; dy++) {
      for (var dx = -radius; dx <= radius; dx++) {
        if (dx == 0 && dy == 0) {
          continue;
        }

        var x = (int)centerX + dx;
        var y = (int)centerY + dy;
        if (x < 0 || y < 0 || x >= size || y >= size) {
          continue;
        }

        action(grid.GridPositionToIndex(x, y));
      }
    }
  }

  /// <summary>
  /// Chebyshev distance between two tiles, so diagonals count as 1
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private int Distance(uint a, uint b) {
    grid.IndexToGridPosition(a, out var ax, out var ay);
    grid.IndexToGridPosition(b, out var bx, out var by);
    return Math.Max(Math.Abs((int)ax - (int)bx), Math.Abs((int)ay - (int)by));
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static bool IsLand(TileKind kind) => kind is not TileKind.Water and not TileKind.Ocean;
}
