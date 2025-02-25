namespace OpenPolytopia;

using System;
using Godot;

public struct Grid(uint size) {
  private readonly Tile[] _grid = new Tile[size * size];

  public Tile GetTile(Vector2I position) => _grid[(position.Y * size) + position.X];
}

public struct Tile {
  private const int ROAD_POSITION = 63;
  private const int RUIN_POSITION = 62;
  private const int TILEKIND_POSITION = 59;
  private const int TILE_MODIFIER_POSITION = 57;
  private const int TILE_BUILDINGS_POSITION = 54;

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
  /// Creates a new tile from a <see cref="TileKind"/>
  /// </summary>
  /// <param name="kind">the type of tile to create</param>
  /// <returns>a new tile</returns>
  public static Tile FromTileKind(TileKind kind) => new() { _inner = (ulong)kind << TILEKIND_POSITION };

  /// <summary>
  /// Checks if the tile has a road/bridge
  /// </summary>
  /// <returns>whether there's a road or a bridge on this tile</returns>
  public bool HasRoad() => _inner >> ROAD_POSITION == 1;

  /// <summary>
  /// Sets the road for this tile
  /// </summary>
  /// <param name="road">1: yes road; 0: no road</param>
  public void SetRoad(bool road) => _inner |= road.ToULong() << ROAD_POSITION;

  /// <summary>
  /// Checks if the tile has a ruin
  /// </summary>
  /// <returns>whether there's a ruin or not</returns>
  public bool HasRuin() => ((_inner >> RUIN_POSITION) & ONE_BIT) == 1;

  /// <summary>
  /// Sets the ruin for this tile
  /// </summary>
  /// <param name="ruin">1: yes ruin; 0: no ruin</param>
  public void SetRuin(bool ruin) => _inner |= ruin.ToULong() << RUIN_POSITION;

  /// <summary>
  /// Gets the tile's type
  /// </summary>
  /// <returns>the tile's type</returns>
  public TileKind GetTileKind() => (TileKind)((_inner >> TILEKIND_POSITION) & THREE_BITS);

  /// <summary>
  /// Returns the tile modifier
  /// </summary>
  /// <returns>the tile modifier as an <c>int</c></returns>
  public int GetTileModifier() => (int)((_inner >> TILE_MODIFIER_POSITION) & TWO_BITS);

  /// <summary>
  /// Returns the tile modifier
  /// </summary>
  /// <typeparam name="T">The tile modifier enum</typeparam>
  /// <returns>the tile modifier as <c>T</c></returns>
  public T GetTileModifier<T>() => (T)(object)GetTileModifier();
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

public enum FieldTileBuildings {
  None = 0,
  Sawmill = 1,
  Windmill = 2,
  Market = 3,
  Temple = 4,
  Forge = 5,
  Altar = 6
}

public static class BooleanExtensions {
  public static ulong ToULong(this bool value) => value ? 1ul : 0;
}
