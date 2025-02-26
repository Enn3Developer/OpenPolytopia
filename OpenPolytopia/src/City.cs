namespace OpenPolytopia;

using System;
using System.Collections.Generic;

public class CityManager(Grid grid) {
  private readonly List<uint> _cities = [];

  public CityData this[uint id] {
    get => grid[_cities[(int)(id - 1)]].GetCustomData<CityData>();
    set => grid.ModifyTile(_cities[(int)(id - 1)], (ref Tile tile) => tile.SetCustomData(value));
  }

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

  public void ModifyCity(uint id, ActionRef<CityData> callback) {
    var cityData = grid[_cities[(int)(id - 1)]].GetCustomData<CityData>();
    callback(ref cityData);
    grid.ModifyTile(_cities[(int)(id - 1)], (ref Tile tile) => tile.SetCustomData(cityData));
  }
}

public struct CityData : ITileCustomData {
  private const int OWNER_POSITION = 27;
  private const int LEVEL_POSITION = 22;
  private const int POPULATION_POSITION = 17;
  private const int TROOPS_POSITION = 12;
  private const int PARKS_POSITION = 7;
  private const int WALL_POSITION = 6;
  private const int FORGE_POSITION = 5;

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
  /// Using 0 as the left-most bit and 63 as the right-most bit
  /// <list type="bullet">
  /// <item>[0, 4] -> City owner; if 0 no owner</item>
  /// <item>[5, 9] -> City level; if 0 the city is a village</item>
  /// <item>[10, 14] -> Current population</item>
  /// <item>[15, 19] -> Current troops</item>
  /// <item>[20, 24] -> Parks number</item>
  /// <item>25 -> Has wall</item>
  /// <item>26 -> Has forge</item>
  /// </list>
  /// </summary>
  private uint _inner;

  public int Owner {
    get => (int)_inner.GetBits(FOUR_BITS, OWNER_POSITION);
    set => _inner.SetBits((uint)value, FOUR_BITS, OWNER_POSITION);
  }

  public int Level {
    get => (int)_inner.GetBits(FIVE_BITS, LEVEL_POSITION);
    set => _inner.SetBits((uint)value, FIVE_BITS, LEVEL_POSITION);
  }

  public int MaxPopulation => Level + 1;

  public int Population {
    get => (int)_inner.GetBits(FIVE_BITS, POPULATION_POSITION);
    set => _inner.SetBits((uint)value, FIVE_BITS, POPULATION_POSITION);
  }

  public int Troops {
    get => (int)_inner.GetBits(FIVE_BITS, TROOPS_POSITION);
    set => _inner.SetBits((uint)value, FIVE_BITS, TROOPS_POSITION);
  }

  public int Parks {
    get => (int)_inner.GetBits(FIVE_BITS, PARKS_POSITION);
    set => _inner.SetBits((uint)value, FIVE_BITS, PARKS_POSITION);
  }

  public bool Wall {
    get => _inner.GetBits(ONE_BIT, WALL_POSITION) == 1;
    set => _inner.SetBits(value.ToUInt(), ONE_BIT, WALL_POSITION);
  }

  public bool Forge {
    get => _inner.GetBits(ONE_BIT, FORGE_POSITION) == 1;
    set => _inner.SetBits(value.ToUInt(), ONE_BIT, FORGE_POSITION);
  }

  public void FromULong(ulong value) => _inner = (uint)(value & MAX_BITS);

  public ulong ToULong() => _inner;
}
