namespace OpenPolytopia.Common;

using System.Runtime.CompilerServices;

public enum BridgeDirection {
  Vertical = 0,
  Horizontal = 1
}

public struct BridgeData : ITileCustomData {
  /// <summary>
  /// Representation of bridge direction.
  ///
  /// Using 0 and 1 bit
  /// <list type="bullet">
  /// <item>0 -> Direction is vertical</item>
  /// <item>1 -> Direction is horizontal</item>
  /// </list>
  /// </summary>
  public BridgeDirection Direction { get; set; }

  private const int ONE_BIT = 1;

  /// <summary>
  /// Deserializes a <see langword="ulong"/> to a <see cref="BridgeDirection"/>
  /// </summary>
  /// <param name="value">value to deserialize from</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void FromULong(ulong value) => Direction = (BridgeDirection)value.GetBits(ONE_BIT, 0);

  /// <summary>
  /// Serializes the data to a <see langword="ulong"/>
  /// </summary>
  /// <returns>the data serialized</returns>
  public ulong ToULong() {
    var value = 0ul;
    value.SetBits((ulong)Direction, ONE_BIT, 0);
    return value;
  }
}
