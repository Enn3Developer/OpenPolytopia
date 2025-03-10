namespace OpenPolytopia.Common;

public abstract class Tribe {
  public abstract ResourceType GuaranteedResources { get; }

  public abstract TileKind GuaranteedTiles { get; }

  public abstract StartingTech StartingTech { get; }
}

/// <summary>
/// Tribes enum
/// </summary>
/// <remarks>
/// There can only be max 32 tribes
/// </remarks>
public enum TribeType {
  Imperius = 0,
  Bardur = 1,
  Oumaji = 2,
  Kickoo = 3,
  Vengir = 4,
  Elyrion = 5,
}

public enum ResourceType {
  None,
  Fruit,
  Crop,
  Animal,
  Fish,
  Mineral,
}

/// <summary>
/// Available wonders to build
/// </summary>
/// <remarks>
/// At the moment, only 7 wonders max can be built.
/// <br/>
/// Because this is used internally in <see cref="Tile"/> there is a null option at index 0 (<see cref="Wonder.None"/>)
/// </remarks>
public enum Wonder {
  None = 0,
  ParkOfFortune = 1,
  AltarOfPeace = 2,
  TowerOfWisdom = 3,
  GrandBazaar = 4,
  GateOfPower = 5,
  EmperorsTomb = 6,
  EyeTower = 7,
}

#region Tribe implementations

public class ImperiusTribe : Tribe {
  public override ResourceType GuaranteedResources => ResourceType.Fruit;
  public override TileKind GuaranteedTiles => TileKind.Field;
  public override StartingTech StartingTech => new(BranchType.Organization, "organization");
}

public class BardurTribe : Tribe {
  public override ResourceType GuaranteedResources => ResourceType.Fruit;
  public override TileKind GuaranteedTiles => TileKind.Mountain;
  public override StartingTech StartingTech => new(BranchType.Hunting, "hunting");
}

public class OumajiTribe : Tribe {
  public override ResourceType GuaranteedResources => ResourceType.Animal;
  public override TileKind GuaranteedTiles => TileKind.Mountain;
  public override StartingTech StartingTech => new(BranchType.Riding, "riding");
}

public class KickooTribe : Tribe {
  public override ResourceType GuaranteedResources => ResourceType.Fish;
  public override TileKind GuaranteedTiles => TileKind.Water;
  public override StartingTech StartingTech => new(BranchType.Fishing, "fishing");
}

public class VengirTribe : Tribe {
  public override ResourceType GuaranteedResources => ResourceType.Mineral;
  public override TileKind GuaranteedTiles => TileKind.Mountain;
  public override StartingTech StartingTech => new(BranchType.Climbing, "smithery");
}

public class ElyrionTribe : Tribe {
  public override ResourceType GuaranteedResources => ResourceType.Animal;
  public override TileKind GuaranteedTiles => TileKind.Field;
  public override StartingTech StartingTech => new(BranchType.Hunting, "hunting");
}

#endregion
