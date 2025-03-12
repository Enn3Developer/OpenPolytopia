namespace OpenPolytopia.Common;

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Godot;

/// <summary>
/// Represents a squared grid <c>size * size</c>
/// </summary>
/// <param name="size">the width of the grid</param>
public class Grid(uint size) {
  private readonly Tile[] _grid = new Tile[size * size];

  /// <summary>
  /// Get size of grid
  /// </summary>
  public uint Size => size;

  /// <summary>
  /// Returns a <see cref="Tile"/> in the given position.
  /// </summary>
  /// <remarks>
  /// Don't modify the tile returned by this because it won't be modified, use <see cref="ModifyTile(Vector2I, ActionRef&lt;Tile&gt;)"/>
  /// or set the value back
  /// <code>
  /// var position = new Vector2I(0, 0);
  /// var tile = grid[position];
  /// tile.Roads = true;
  /// grid[position] = tile;
  /// </code>
  /// </remarks>
  /// <param name="position">the position where to get the tile</param>
  public Tile this[Vector2I position] {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _grid[(position.Y * size) + position.X];
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _grid[(position.Y * size) + position.X] = value;
  }

  /// <summary>
  /// Returns a <see cref="Tile"/> in the given position.
  /// </summary>
  /// <remarks>
  /// Don't modify the tile returned by this because it won't be modified, use <see cref="ModifyTile(uint, ActionRef&lt;Tile&gt;)"/>
  /// or set the value back
  /// <code>
  /// var tile = grid[0];
  /// tile.Roads = true;
  /// grid[0] = tile;
  /// </code>
  /// </remarks>
  /// <param name="index">index of the tile</param>
  public Tile this[uint index] {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _grid[index];
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _grid[index] = value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public uint GridPositionToIndex(Vector2I position) => (uint)((position.Y * size) + position.X);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public uint GridPositionToIndex(int x, int y) => (uint)((y * size) + x);

  /// <summary>
  /// Modifies a given tile
  /// </summary>
  /// <example>
  /// <code>
  /// var position = new Vector2I(0, 0);
  /// grid.ModifyTile(position, (ref Tile tile) => tile.Roads = true);
  /// </code>
  /// </example>
  /// <param name="position">position of the tile in the grid</param>
  /// <param name="callback">callback function to modify the tile</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void ModifyTile(Vector2I position, ActionRef<Tile> callback) =>
    callback(ref _grid[(position.Y * size) + position.X]);

  /// <summary>
  /// Modifies a given tile
  /// </summary>
  /// <example>
  /// <code>
  /// grid.ModifyTile(0, (ref Tile tile) => tile.Roads = true);
  /// </code>
  /// </example>
  /// <param name="index">index of the tile</param>
  /// <param name="callback">callback function to modify the tile</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void ModifyTile(uint index, ActionRef<Tile> callback) =>
    callback(ref _grid[index]);

  /// <summary>
  /// Modifies multiple tiles with the same callback using SIMD if possible
  /// </summary>
  /// <remarks>
  /// May be worse performance-wise than looping for every position and using ModifyTile if the array is small enough
  /// </remarks>
  /// <example>
  /// <code>
  /// var positions = [new Vector2I(0, 0), new Vector2I(1, 0), new Vector2I(0, 1), new Vector2I(1, 1)];
  /// grid.ModifyMultipleTiles(positions, (ref Tile tile) => tile.Owner = 1)
  /// </code>
  /// </example>
  /// <param name="positions">the tile positions array</param>
  /// <param name="callback">callback function to modify a single tile</param>
  public void ModifyMultipleTiles(Vector2I[] positions, ActionRef<Tile> callback) {
    var xPositions = new int[positions.Length];
    var yPositions = new int[positions.Length];

    for (var i = 0; i < positions.Length; i++) {
      xPositions[i] = positions[i].X;
      yPositions[i] = positions[i].Y;
    }

    ModifyMultipleTiles(xPositions, yPositions, positions, callback);
  }

  public void ModifyMultipleTiles(int[] xPositions, int[] yPositions, ActionRef<Tile> callback) {
    var length = xPositions.Length;
    var remaining = length % Vector<int>.Count;

    for (var i = 0; i < length - remaining; i += Vector<int>.Count) {
      var v1 = new Vector<int>(xPositions, i);
      var v2 = new Vector<int>(yPositions, i);
      ModifyMultipleTiles(v1, v2, callback);
    }

    for (var i = length - remaining; i < length; i++) {
      ModifyTile(GridPositionToIndex(xPositions[i], yPositions[i]), callback);
    }
  }

  public void ModifyMultipleTiles(int[] xPositions, int[] yPositions, Vector2I[] positions, ActionRef<Tile> callback) {
    var length = xPositions.Length;
    var remaining = length % Vector<int>.Count;

    for (var i = 0; i < length - remaining; i += Vector<int>.Count) {
      var v1 = new Vector<int>(xPositions, i);
      var v2 = new Vector<int>(yPositions, i);
      ModifyMultipleTiles(v1, v2, callback);
    }

    for (var i = length - remaining; i < length; i++) {
      ModifyTile(positions[i], callback);
    }
  }

  public void ModifyMultipleTiles(Vector<int> xPositions, Vector<int> yPositions, ActionRef<Tile> callback) {
    var indexes = (yPositions * (int)size) + xPositions;
    var indexesArray = new int[Vector<int>.Count];
    indexes.CopyTo(indexesArray);
    foreach (var i in indexesArray) {
      callback(ref _grid[i]);
    }
  }
}

/// <summary>
/// Represents a single tile with various setter/getter for accessing its internal data
/// </summary>
public struct Tile {
  private const int ROAD_POSITION = 63;
  private const int RUIN_POSITION = 62;
  private const int TILEKIND_POSITION = 59;
  private const int TILE_MODIFIER_POSITION = 57;
  private const int TILE_BUILDING_POSITION = 54;
  private const int TILE_OWNER_POSITION = 50;
  private const int TILE_BIOME_POSITION = 45;
  private const int CITY_POSITION = 37;
  private const int WONDER_POSITION = 33;
  private const int CUSTOM_DATA_POSITION = 0;

  private const int ONE_BIT = 1;
  private const int TWO_BITS = 3;
  private const int THREE_BITS = 7;
  private const int FOUR_BITS = 15;
  private const int FIVE_BITS = 31;
  private const int SIX_BITS = 63;
  private const int SEVEN_BITS = 127;
  private const int EIGHT_BITS = 255;

  /// <summary>
  /// Max bits available for custom data for tiles
  /// </summary>
  /// <remarks>
  /// This is represented as the number of bits set to 1, not as the number itself
  /// </remarks>
  public const ulong MAX_CUSTOM_DATA_BITS = 8_589_934_591;

  /// <summary>
  /// Returns the corresponding modifier enum's <see cref="Type"/> from a <see cref="Kind"/>
  /// </summary>
  /// <param name="kind">the tile kind</param>
  /// <returns>the modifier enum</returns>
  /// <exception cref="ArgumentOutOfRangeException">if <c>kind</c> isn't valid</exception>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static Type GetModifier(TileKind kind) =>
    kind switch {
      TileKind.Field => typeof(FieldTileModifier),
      TileKind.Mountain => typeof(MountainTileModifier),
      TileKind.Forest => typeof(ForestTileModifier),
      TileKind.Water => typeof(WaterTileModifier),
      TileKind.Ocean => typeof(OceanTileModifier),
      TileKind.Village => typeof(VillageTileModifier),
      _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"kind is invalid: {kind}")
    };

  /// <summary>
  /// Inner representation of a tile.
  ///
  /// Using 0 as the left-most bit and 63 as the right-most bit
  /// <list type="bullet">
  /// <item>0 -> 1: has road; 0 doesn't have any road; (bridge if on water)</item>
  /// <item>1 -> 1: has ancient ruin; 0 doesn't have any ruin</item>
  /// <item>[2, 4] -> <see cref="Kind"/></item>
  /// <item>[5, 6] -> Tile modifier</item>
  /// <item>[7, 9] -> Tile buildings</item>
  /// <item>[10, 13] -> Tile owner; if 0 no owner</item>
  /// <item>[14, 18] -> Tribe biome</item>
  /// <item>[19, 26] -> City ID; if 0 no city</item>
  /// <item>[27, 30] -> Wonder</item>
  /// <item>[31, 63] -> Custom data</item>
  /// </list>
  /// </summary>
  private ulong _inner;

  /// <summary>
  /// Roads/bridge on top of this tile
  /// </summary>
  public bool Roads {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(ONE_BIT, ROAD_POSITION) == 1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value.ToULong(), ONE_BIT, ROAD_POSITION);
  }

  /// <summary>
  /// Ruin on top of this tile
  /// </summary>
  public bool Ruin {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(ONE_BIT, RUIN_POSITION) == 1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value.ToULong(), ONE_BIT, RUIN_POSITION);
  }

  /// <summary>
  /// The type of the tile
  /// </summary>
  public TileKind Kind => (TileKind)_inner.GetBits(THREE_BITS, TILEKIND_POSITION);

  /// <summary>
  /// The tile modifier castable to the corresponding enum
  /// </summary>
  public int Modifier {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (int)_inner.GetBits(TWO_BITS, TILE_MODIFIER_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((ulong)value, TWO_BITS, TILE_MODIFIER_POSITION);
  }

  /// <summary>
  /// The tile building castable to the corresponding enum
  /// </summary>
  public int Building {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (int)_inner.GetBits(THREE_BITS, TILE_BUILDING_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((ulong)value, THREE_BITS, TILE_BUILDING_POSITION);
  }

  /// <summary>
  /// The owner of the tile
  /// </summary>
  /// <remarks>
  /// if 0, it is assumed that this tile has no owners
  /// </remarks>
  public int Owner {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (int)_inner.GetBits(FOUR_BITS, TILE_OWNER_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((ulong)value, FOUR_BITS, TILE_OWNER_POSITION);
  }

  /// <summary>
  /// Biome of the tile based on the tribes available in the game
  /// </summary>
  public TribeType Biome {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (TribeType)_inner.GetBits(FIVE_BITS, TILE_BIOME_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((ulong)value, FIVE_BITS, TILE_BIOME_POSITION);
  }

  /// <summary>
  /// City that owns this tile
  /// </summary>
  /// <remarks>
  /// If 0, it is assumed that no city owns this tile
  /// </remarks>
  public int City {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (int)_inner.GetBits(EIGHT_BITS, CITY_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((ulong)value, EIGHT_BITS, CITY_POSITION);
  }

  /// <summary>
  /// The wonder that has been built on this tile
  /// </summary>
  public Wonder Wonder {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (Wonder)_inner.GetBits(FOUR_BITS, WONDER_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((ulong)value, FOUR_BITS, WONDER_POSITION);
  }

  /// <summary>
  /// Creates a new tile from a <see cref="Kind"/>
  /// </summary>
  /// <param name="kind">the type of tile to create</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Tile(TileKind kind) {
    _inner = (ulong)kind << TILEKIND_POSITION;
  }

  /// <summary>
  /// Returns the tile modifier
  /// </summary>
  /// <typeparam name="T">The tile modifier enum</typeparam>
  /// <returns>the tile modifier as <c>T</c></returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public T GetTileModifier<T>() => (T)(object)Modifier;

  /// <summary>
  /// Sets the tile modifier
  /// </summary>
  /// <param name="tileModifier">the tile modifier enum value</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetTileModifier(object tileModifier) => Modifier = (int)tileModifier;

  /// <summary>
  /// Sets the tile modifier
  /// </summary>
  /// <param name="tileModifier">the tile modifier enum value</param>
  /// <typeparam name="T">The tile modifier enum</typeparam>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetTileModifier<T>(T tileModifier) => Modifier = (int)(object)tileModifier!;

  /// <summary>
  /// Returns the tile building
  /// </summary>
  /// <typeparam name="T">The tile building enum</typeparam>
  /// <returns>the tile building as <c>T</c></returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public T GetTileBuilding<T>() => (T)(object)Building;

  /// <summary>
  /// Sets the tile building
  /// </summary>
  /// <param name="tileBuilding">the tile building enum value</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetTileBuilding(object tileBuilding) => Building = (int)tileBuilding;

  /// <summary>
  /// Sets the tile building
  /// </summary>
  /// <param name="tileBuilding">the tile building enum value</param>
  /// <typeparam name="T">The tile building enum</typeparam>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetTileBuilding<T>(T tileBuilding) => Building = (int)(object)tileBuilding!;

  /// <summary>
  /// Returns the custom data to be saved in this tile
  /// </summary>
  /// <typeparam name="T">The type of the custom data to cast from; must implement <see cref="ITileCustomData"/> and have a constructor without parameters</typeparam>
  /// <returns>the custom data</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public T GetCustomData<T>() where T : ITileCustomData, new() {
    var t = new T();
    t.FromULong(_inner.GetBits(MAX_CUSTOM_DATA_BITS, CUSTOM_DATA_POSITION));
    return t;
  }

  /// <summary>
  /// Sets the custom data for this tile
  /// </summary>
  /// <param name="data">the data to set for this tile</param>
  /// <typeparam name="T">the type of the data</typeparam>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetCustomData<T>(T data) where T : ITileCustomData {
    var value = data.ToULong();
    _inner.SetBits(value, MAX_CUSTOM_DATA_BITS, CUSTOM_DATA_POSITION);
  }
}

/// <summary>
/// Describes the type of the tile without modifiers
/// </summary>
public enum TileKind {
  Field = 0,
  Mountain = 1,
  Forest = 2,
  Water = 3,
  Ocean = 4,
  Village = 5
}

public enum FieldTileModifier {
  Normal = 0,
  Crop = 1,
  Fruit = 2
}

public enum MountainTileModifier {
  Normal = 0,
  Ore = 1
}

public enum ForestTileModifier {
  Normal = 0,
  Animal = 1
}

public enum WaterTileModifier {
  Normal = 0,
  Fish = 1,
  Star = 2
}

public enum OceanTileModifier {
  Normal = 0,
  Fish = 1,
  Star = 2
}

public enum VillageTileModifier {
  Village = 0,
  City = 1
}

public enum FieldTileBuilding {
  None = 0,
  Sawmill = 1,
  Windmill = 2,
  Market = 3,
  Temple = 4,
  Forge = 5,
  Altar = 6,
  Farm = 7
}

public enum ForestTileBuilding {
  None = 0,
  LumberHut = 1,
  Temple = 2
}

public enum MountainTileBuilding {
  None = 0,
  Mine = 1,
  Temple = 2
}

public enum WaterTileBuilding {
  None = 0,
  Port = 1,
  Temple = 2
}

public enum OceanTileBuilding {
  None = 0,
  Temple = 1
}

public enum VillageTileBuilding {
  None = 0
}

public interface ITileCustomData {
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void FromULong(ulong value);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ulong ToULong();
}
