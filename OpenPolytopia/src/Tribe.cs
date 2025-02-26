namespace OpenPolytopia;

/// <summary>
/// Tribes enum
/// </summary>
/// <remarks>
/// There can only be max 32 tribes
/// </remarks>
public enum Tribe {
  Imperius = 0,
  Bardur = 1,
  Oumaji = 2,
  Kickoo = 3,
  Vengir = 4,
  Elyrion = 5,
  ElyrionDark = 6
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
