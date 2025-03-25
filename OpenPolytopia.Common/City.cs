namespace OpenPolytopia.Common;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

/// <summary>
/// Layer of abstraction to manage all cities in a grid
/// </summary>
public class CityManager(Grid grid) {
  private readonly List<uint> _cities = [];

  public IEnumerable<uint> Cities => _cities;

  /// <summary>
  /// Access to the <see cref="CityData"/>
  /// </summary>
  /// <remarks>
  /// Exactly as <see cref="Grid.this[Vector2I]"/>, you can't modify directly the <see cref="CityData"/> that this getter returns,
  /// use <see cref="ModifyCity"/> for that
  /// </remarks>
  /// <param name="id">id of the city</param>
  public CityData this[uint id] {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => grid[_cities[(int)(id - 1)]].GetCustomData<CityData>();
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => grid.ModifyTile(_cities[(int)(id - 1)], (ref Tile tile) => tile.SetCustomData(value));
  }

  /// <summary>
  /// Returns the grid id for a city
  /// </summary>
  /// <param name="id">the id of the city</param>
  /// <returns>the id of the tile in the grid</returns>
  /// <example>
  /// <code>
  /// var index = cityManager.GetIndex(cityId);
  /// grid[index].Owner = 2;
  /// </code>
  /// </example>
  public uint GetIndex(uint id) => _cities[(int)(id - 1)];

  /// <summary>
  /// Registers a tile as a city
  /// </summary>
  /// <param name="position">position of the tile in the grid</param>
  /// <returns>the id of the city</returns>
  /// <example>
  /// <code>
  /// var position = new Vector2I(0, 5);
  /// var cityId = cityManager.RegisterCity(position);
  /// </code>
  /// </example>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public uint RegisterCity(Vector2I position) => RegisterCity(grid.GridPositionToIndex(position));

  /// <summary>
  /// Registers a tile as a city
  /// </summary>
  /// <param name="index">index of the tile in the grid</param>
  /// <returns>the id of the city</returns>
  /// <exception cref="ArgumentOutOfRangeException">because of internal implementation, there can only be 255 cities</exception>
  /// <example>
  /// <code>
  /// var position = new Vector2I(0, 5);
  /// var index = grid.GridPositionToIndex(position);
  /// var cityId = cityManager.RegisterCity(index);
  /// </code>
  /// </example>
  public uint RegisterCity(uint index) {
    if (_cities.Count == 255) {
      throw new ArgumentOutOfRangeException(nameof(index), index, "reached maximum cities possible; max is 255");
    }

    _cities.Add(index);
    grid.ModifyTile(index, (ref Tile tile) => {
      tile.City = _cities.Count;
      tile.SetCustomData(new CityData());
    });
    return (uint)_cities.Count;
  }

  /// <summary>
  /// Modifies the <see cref="CityData"/> inside the city tile
  /// </summary>
  /// <example>
  /// <code>
  /// var position = new Vector2I(0, 5);
  /// var id = cityManager.RegisterCity(position);
  /// cityManager.ModifyCity(id, (ref CityData cityData) => cityData.Owner = 2);
  /// </code>
  /// </example>
  /// <param name="id">id of the city</param>
  /// <param name="callback">function callback to modify the data</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void ModifyCity(uint id, ActionRef<CityData> callback) {
    var cityData = grid[_cities[(int)(id - 1)]].GetCustomData<CityData>();
    callback(ref cityData);
    grid.ModifyTile(_cities[(int)(id - 1)], (ref Tile tile) => tile.SetCustomData(cityData));
  }

  /// <summary>
  /// Easy method to set the city as a capital
  /// </summary>
  /// <param name="id">the id of the city</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetCapital(uint id) => ModifyCity(id, (ref CityData cityData) => cityData.Capital = true);
}

/// <summary>
/// Custom data for a tile, made to manage cities easier
/// </summary>
public struct CityData : ITileCustomData {
  private const int OWNER_POSITION = 27;
  private const int LEVEL_POSITION = 22;
  private const int POPULATION_POSITION = 17;
  private const int TROOPS_POSITION = 12;
  private const int PARKS_POSITION = 7;
  private const int WALL_POSITION = 6;
  private const int FORGE_POSITION = 5;
  private const int CAPITAL_POSITION = 4;
  private const int CONNECTED_POSITION = 3;

  private const int ONE_BIT = 1;
  private const int TWO_BITS = 3;
  private const int THREE_BITS = 7;
  private const int FOUR_BITS = 15;
  private const int FIVE_BITS = 31;
  private const int SIX_BITS = 63;
  private const int SEVEN_BITS = 127;
  private const int EIGHT_BITS = 255;
  private const uint MAX_BITS = 4_294_967_295;

  /// <summary>
  /// Inner representation of a tile.
  ///
  /// Using 0 as the left-most bit and 31 as the right-most bit
  /// <list type="bullet">
  /// <item>[0, 4] -> City owner; if 0 no owner</item>
  /// <item>[5, 9] -> City level; if 0 the city is a village</item>
  /// <item>[10, 14] -> Current population</item>
  /// <item>[15, 19] -> Current troops</item>
  /// <item>[20, 24] -> Parks number</item>
  /// <item>25 -> Has wall</item>
  /// <item>26 -> Has forge</item>
  /// <item>27 -> Is capital</item>
  /// <item>28 -> Is connected</item>
  /// </list>
  /// </summary>
  private uint _inner;

  /// <summary>
  /// Owner of the city
  /// </summary>
  /// <remarks>
  /// If 0, the city has no owner, thus it is a village
  /// </remarks>
  public int Owner {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (int)_inner.GetBits(FOUR_BITS, OWNER_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((uint)value, FOUR_BITS, OWNER_POSITION);
  }

  /// <summary>
  /// Gets the level of the city
  /// </summary>
  /// <remarks>
  /// If 0, this is a village
  /// </remarks>
  public int Level {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (int)_inner.GetBits(FIVE_BITS, LEVEL_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((uint)value, FIVE_BITS, LEVEL_POSITION);
  }

  /// <summary>
  /// Max population that this city has and requires to level up
  /// </summary>
  public int MaxPopulation => Level + 1;

  /// <summary>
  /// Current population that this city has
  /// </summary>
  public int Population {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (int)_inner.GetBits(FIVE_BITS, POPULATION_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((uint)value, FIVE_BITS, POPULATION_POSITION);
  }

  /// <summary>
  /// Number of troops that have this city as their origin city
  /// </summary>
  public int Troops {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (int)_inner.GetBits(FIVE_BITS, TROOPS_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((uint)value, FIVE_BITS, TROOPS_POSITION);
  }

  /// <summary>
  /// Number of parks build on this city
  /// </summary>
  public int Parks {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (int)_inner.GetBits(FIVE_BITS, PARKS_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((uint)value, FIVE_BITS, PARKS_POSITION);
  }

  /// <summary>
  /// Whether this city has a wall surrounding it
  /// </summary>
  public bool Wall {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(ONE_BIT, WALL_POSITION) == 1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value.ToUInt(), ONE_BIT, WALL_POSITION);
  }

  /// <summary>
  /// Whether a Forge was built on this city
  /// </summary>
  public bool Forge {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(ONE_BIT, FORGE_POSITION) == 1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value.ToUInt(), ONE_BIT, FORGE_POSITION);
  }

  /// <summary>
  /// Whether this city is a capital
  /// </summary>
  public bool Capital {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(ONE_BIT, CAPITAL_POSITION) == 1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value.ToUInt(), ONE_BIT, CAPITAL_POSITION);
  }

  /// <summary>
  /// Whether this city is connected
  /// </summary>
  public bool Connected {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(ONE_BIT, CONNECTED_POSITION) == 1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value.ToUInt(), ONE_BIT, CONNECTED_POSITION);
  }

  /// <summary>
  /// How many stars does this city produce
  /// </summary>
  /// <remarks>
  /// If this is a village, it returns 0.
  /// <br/>
  /// This doesn't manage edge cases like invaded cities
  /// </remarks>
  public int Stars => Level > 0 ? Level + Capital.ToInt() + Forge.ToInt() + Parks : 0;

  /// <summary>
  /// Sets all the data necessary for when the city has levelled up
  /// </summary>
  /// <remarks>
  /// This only sets the necessary data, it doesn't do anything else so you have to manage if the player wants to build
  /// a wall, a forge, etc...
  /// </remarks>
  /// <returns>If the level up is valid or not</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool LevelUp() {
    if (Population < MaxPopulation) {
      return false;
    }

    Level += 1;
    Population = 0;
    return true;
  }

  /// <summary>
  /// Deserializes a <see langword="ulong"/> to a <see cref="CityData"/>
  /// </summary>
  /// <param name="value">value to deserialize from</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void FromULong(ulong value) => _inner = (uint)(value & MAX_BITS);

  /// <summary>
  /// Serializes the data to a <see langword="ulong"/>
  /// </summary>
  /// <returns>the data serialized</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ulong ToULong() => _inner;
}
