namespace OpenPolytopia.Common;

public enum BridgeDirection {
  Vertical = 0,
  Horizontal = 1
}

public struct BridgeData : ITileCustomData {

  public BridgeDirection Direction { get; set; }

  private const int ONE_BIT = 1;

  public void FromULong(ulong value) {
    var direction = (BridgeDirection) value.GetBits(ONE_BIT, 0);
  }

  public ulong ToULong() {
    var value = 0ul;
    value.SetBits((ulong)Direction, ONE_BIT, 0);
    return value;
  }
}
