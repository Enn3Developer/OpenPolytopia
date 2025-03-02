namespace OpenPolytopia

module ScoreTypeModule =
  type ScoreType =
    | DiscoveredTile
    | ClaimedTile
    | TroopSpawned of number: uint * stars: uint
    | CityLevelUp of numberOfLevels: uint
    | VillageConquered
    | TemplesBuilt
    | DiscoveredNewTech
    | ParkBuilt
    | MonumentsBuilt
    | TemplesDestroyed
    | LoseTroop of stars: uint
    | ParkDestroyed
    | MonumentsDestroyed
    | LoseCity of cityLevel: uint

  let rec scoreTypeToInt scoreType =
    match scoreType with
    | DiscoveredTile -> 5
    | ClaimedTile -> 20
    | TroopSpawned(number, stars) -> int (number * (stars * 5u + 5u))
    | CityLevelUp numberOfLevels -> int (50u * numberOfLevels)
    | VillageConquered -> 100
    | TemplesBuilt -> 100
    | DiscoveredNewTech -> 100
    | ParkBuilt -> 200
    | MonumentsBuilt -> 400
    | TemplesDestroyed -> -100
    | LoseTroop stars -> scoreTypeToInt (TroopSpawned(1u, stars))
    | ParkDestroyed -> -200
    | MonumentsDestroyed -> -400
    | LoseCity cityLevel -> -(scoreTypeToInt VillageConquered + scoreTypeToInt (CityLevelUp cityLevel))
