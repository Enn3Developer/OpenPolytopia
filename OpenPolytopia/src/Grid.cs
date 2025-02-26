namespace OpenPolytopia;

using System;
using Godot;

public delegate void ActionRef<T>(ref T item);

public struct Grid(uint size) {
  private readonly Tile[] _grid = new Tile[size * size];

  /// <summary>
  /// Returns a <see cref="Tile"/> in the given position.
  /// </summary>
  /// <remarks>
  /// Don't modify the tile returned by this because it won't be modified, use <see cref="ModifyTile"/>
  /// </remarks>
  /// <param name="position">the position where to get the tile</param>
  public Tile this[Vector2I position] {
    get => _grid[(position.Y * size) + position.X];
    set => _grid[(position.Y * size) + position.X] = value;
  }

  /// <summary>
  /// Modifies a given tile
  /// </summary>
  /// <param name="position">position of the tile in the grid</param>
  /// <param name="callback">callback function to modify the tile</param>
  public void ModifyTile(Vector2I position, ActionRef<Tile> callback) =>
    callback(ref _grid[(position.Y * size) + position.X]);
}

public struct Tile {
  private const int ROAD_POSITION = 63;
  private const int RUIN_POSITION = 62;
  private const int TILEKIND_POSITION = 59;
  private const int TILE_MODIFIER_POSITION = 57;
  private const int TILE_BUILDING_POSITION = 54;

  private const int ONE_BIT = 1;
  private const int TWO_BITS = 3;
  private const int THREE_BITS = 7;
  private const int FOUR_BITS = 15;
  private const int FIVE_BITS = 31;

  /// <summary>
  /// Returns the corresponding modifier enum's <see cref="Type"/> from a <see cref="TileKind"/>
  /// </summary>
  /// <param name="kind">the tile kind</param>
  /// <returns>the modifier enum</returns>
  /// <exception cref="ArgumentOutOfRangeException">if <c>kind</c> isn't valid</exception>
  public static Type GetModifier(TileKind kind) =>
    kind switch {
      TileKind.Field => typeof(FieldTileModifier),
      TileKind.Mountain => typeof(MountainTileModifier),
      TileKind.Forest => typeof(ForestTileModifier),
      TileKind.Water => typeof(WaterTileModifier),
      TileKind.Ocean => typeof(OceanTileModifier),
      TileKind.Village => typeof(VillageTileModifier),
      _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

  /// <summary>
  /// Inner representation of a tile.
  ///
  /// Using 0 as the left-most bit and 63 as the right-most bit
  /// <list type="bullet">
  /// <item>0 -> 1: has road; 0 doesn't have any road; (bridge if on water)</item>
  /// <item>1 -> 1: has ancient ruin; 0 doesn't have any ruin</item>
  /// <item>[2, 4] -> <see cref="TileKind"/></item>
  /// <item>[5, 6] -> Tile modifier</item>
  /// <item>[7, 9] -> Tile buildings</item>
  /// </list>
  /// </summary>
  private ulong _inner;

  /// <summary>
  /// Roads/bridge on top of this tile
  /// </summary>
  public bool Roads {
    get => _inner.GetBits(ONE_BIT, ROAD_POSITION) == 1;
    set => _inner.SetBits(value.ToULong(), ONE_BIT, ROAD_POSITION);
  }

  /// <summary>
  /// Ruin on top of this tile
  /// </summary>
  public bool Ruin {
    get => _inner.GetBits(ONE_BIT, RUIN_POSITION) == 1;
    set => _inner.SetBits(value.ToULong(), ONE_BIT, RUIN_POSITION);
  }

  /// <summary>
  /// The type of the tile
  /// </summary>
  public TileKind TileKind => (TileKind)_inner.GetBits(THREE_BITS, TILEKIND_POSITION);

  /// <summary>
  /// The tile modifier castable to the corresponding enum
  /// </summary>
  public int TileModifier {
    get => (int)_inner.GetBits(TWO_BITS, TILE_MODIFIER_POSITION);
    set => _inner.SetBits((ulong)value, TWO_BITS, TILE_MODIFIER_POSITION);
  }

  /// <summary>
  /// The tile building castable to the corresponding enum
  /// </summary>
  public int TileBuilding {
    get => (int)_inner.GetBits(THREE_BITS, TILE_BUILDING_POSITION);
    set => _inner.SetBits((ulong)value, THREE_BITS, TILE_BUILDING_POSITION);
  }

  /// <summary>
  /// Creates a new tile from a <see cref="TileKind"/>
  /// </summary>
  /// <param name="kind">the type of tile to create</param>
  public Tile(TileKind kind) {
    _inner = (ulong)kind << TILEKIND_POSITION;
  }

  /// <summary>
  /// Returns the tile modifier
  /// </summary>
  /// <typeparam name="T">The tile modifier enum</typeparam>
  /// <returns>the tile modifier as <c>T</c></returns>
  public T GetTileModifier<T>() => (T)(object)TileModifier;

  /// <summary>
  /// Sets the tile modifier
  /// </summary>
  /// <param name="tileModifier">the tile modifier enum value</param>
  public void SetTileModifier(object tileModifier) => TileModifier = (int)tileModifier;

  /// <summary>
  /// Sets the tile modifier
  /// </summary>
  /// <param name="tileModifier">the tile modifier enum value</param>
  /// <typeparam name="T">The tile modifier enum</typeparam>
  public void SetTileModifier<T>(T tileModifier) => TileModifier = (int)(object)tileModifier!;

  /// <summary>
  /// Returns the tile building
  /// </summary>
  /// <typeparam name="T">The tile building enum</typeparam>
  /// <returns>the tile building as <c>T</c></returns>
  public T GetTileBuilding<T>() => (T)(object)TileBuilding;

  /// <summary>
  /// Sets the tile building
  /// </summary>
  /// <param name="tileBuilding">the tile building enum value</param>
  public void SetTileBuilding(object tileBuilding) => TileBuilding = (int)tileBuilding;

  /// <summary>
  /// Sets the tile building
  /// </summary>
  /// <param name="tileBuilding">the tile building enum value</param>
  /// <typeparam name="T">The tile building enum</typeparam>
  public void SetTileBuilding<T>(T tileBuilding) => TileBuilding = (int)(object)tileBuilding!;
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
  Fish = 1
}

public enum OceanTileModifier {
  Normal = 0,
  Fish = 1
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
  Altar = 6
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

public static class BooleanExtensions {
  /// <summary>
  /// Converts the bool to a <see cref="ulong"/>
  /// </summary>
  /// <param name="value">the bool to convert</param>
  /// <returns>1 if value else 0</returns>
  public static ulong ToULong(this bool value) => value ? 1ul : 0;
}

public static class ULongExtensions {
  /// <summary>
  /// Clear the bits in the specified position by the specified length.
  /// Ex. value == 3 (...011); value.ClearBits(1, 1); value == 1 (...001)
  /// </summary>
  /// <param name="value">the number to clear bits</param>
  /// <param name="bits">number of bits to clear; must be all ones</param>
  /// <param name="position">the position where to start clearing bits starting from the right</param>
  public static ulong ClearBits(this ulong value, int bits, int position) => value & ~((ulong)bits << position);

  /// <summary>
  /// Clear bits and set them
  /// </summary>
  /// <param name="value">the number where to set bits</param>
  /// <param name="data">the bits to be set</param>
  /// <param name="bits">number of bits to set; must be all ones</param>
  /// <param name="position">the position where to set bits starting from the right</param>
  public static void SetBits(this ref ulong value, ulong data, int bits, int position) =>
    value = value.ClearBits(bits, position) | (data << position);

  /// <summary>
  /// Get the bits
  /// </summary>
  /// <param name="value">the number where to get bits</param>
  /// <param name="bits">number of bits to get; must be all ones</param>
  /// <param name="position">the position where to get bits starting from the right</param>
  /// <returns></returns>
  public static ulong GetBits(this ulong value, int bits, int position) => (value >> position) & (ulong)bits;
}
