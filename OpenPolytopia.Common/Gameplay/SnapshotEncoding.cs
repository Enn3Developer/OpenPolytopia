namespace OpenPolytopia.Common.Gameplay;

using System;
using System.Buffers.Binary;

/// <summary>
/// Converts the packed arrays of a <see cref="GameSnapshot"/> to and from the <c>BLOB</c> a database column holds
/// </summary>
/// <remarks>
/// The encoding is little endian and has no header: the length of the blob divided by the size of an element is the
/// number of cells, and the caller already knows how many it expects from <see cref="GameSnapshot.GridSize"/>
/// <br/>
/// Little endian is written explicitly instead of relying on the layout of the machine, so a database file written on
/// one architecture restores the same game on another
/// </remarks>
public static class SnapshotEncoding {
  /// <summary>
  /// Packs <see cref="GameSnapshot.Tiles"/> into a blob
  /// </summary>
  /// <param name="tiles">the raw tiles</param>
  /// <returns>the blob, 8 bytes per tile</returns>
  public static byte[] PackTiles(ReadOnlySpan<ulong> tiles) {
    var bytes = new byte[tiles.Length * sizeof(ulong)];
    for (var index = 0; index < tiles.Length; index++) {
      BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(index * sizeof(ulong)), tiles[index]);
    }

    return bytes;
  }

  /// <summary>
  /// Unpacks a blob written by <see cref="PackTiles"/>
  /// </summary>
  /// <param name="bytes">the blob</param>
  /// <returns>the raw tiles</returns>
  /// <exception cref="ArgumentException">if the length of the blob isn't a multiple of 8</exception>
  public static ulong[] UnpackTiles(ReadOnlySpan<byte> bytes) {
    if (bytes.Length % sizeof(ulong) != 0) {
      throw new ArgumentException(
        $"a tile blob is {sizeof(ulong)} bytes per tile, got {bytes.Length} bytes", nameof(bytes));
    }

    var tiles = new ulong[bytes.Length / sizeof(ulong)];
    for (var index = 0; index < tiles.Length; index++) {
      tiles[index] = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(index * sizeof(ulong))..]);
    }

    return tiles;
  }

  /// <summary>
  /// Packs <see cref="GameSnapshot.Troops"/> into a blob
  /// </summary>
  /// <param name="troops">the raw troops</param>
  /// <returns>the blob, 4 bytes per troop</returns>
  public static byte[] PackTroops(ReadOnlySpan<uint> troops) {
    var bytes = new byte[troops.Length * sizeof(uint)];
    for (var index = 0; index < troops.Length; index++) {
      BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)), troops[index]);
    }

    return bytes;
  }

  /// <summary>
  /// Unpacks a blob written by <see cref="PackTroops"/>
  /// </summary>
  /// <param name="bytes">the blob</param>
  /// <returns>the raw troops</returns>
  /// <exception cref="ArgumentException">if the length of the blob isn't a multiple of 4</exception>
  public static uint[] UnpackTroops(ReadOnlySpan<byte> bytes) {
    if (bytes.Length % sizeof(uint) != 0) {
      throw new ArgumentException(
        $"a troop blob is {sizeof(uint)} bytes per troop, got {bytes.Length} bytes", nameof(bytes));
    }

    var troops = new uint[bytes.Length / sizeof(uint)];
    for (var index = 0; index < troops.Length; index++) {
      troops[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(index * sizeof(uint))..]);
    }

    return troops;
  }

  /// <summary>
  /// Packs <see cref="GameSnapshot.CityIndexes"/> into a blob
  /// </summary>
  /// <param name="cityIndexes">the grid index of every city, ordered by city id</param>
  /// <returns>the blob, 4 bytes per city</returns>
  public static byte[] PackCityIndexes(ReadOnlySpan<uint> cityIndexes) => PackTroops(cityIndexes);

  /// <summary>
  /// Unpacks a blob written by <see cref="PackCityIndexes"/>
  /// </summary>
  /// <param name="bytes">the blob</param>
  /// <returns>the grid index of every city, ordered by city id</returns>
  /// <exception cref="ArgumentException">if the length of the blob isn't a multiple of 4</exception>
  public static uint[] UnpackCityIndexes(ReadOnlySpan<byte> bytes) => UnpackTroops(bytes);
}
