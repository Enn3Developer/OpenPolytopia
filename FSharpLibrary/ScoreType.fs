namespace FSharpLibrary

module ScoreTypeModule =
  /// Type defining a possible score type
  type ScoreType =
    | DiscoveredTile
    | ClaimedTile
    /// Defines how many troops you have spawned and how many stars they costed
    | TroopSpawned of number: uint * stars: uint
    /// Defines how many level up the city did
    | CityLevelUp of numberOfLevels: uint
    | VillageConquered
    | TemplesBuilt
    | DiscoveredNewTech
    | ParkBuilt
    | MonumentsBuilt
    | TemplesDestroyed
    /// Defines how many stars cost a troop that was deleted
    | LoseTroop of stars: uint
    | ParkDestroyed
    | MonumentsDestroyed
    /// Defines at which level the city was
    | LoseCity of cityLevel: uint

  /// Converts a <see cref="ScoreType"/> to an int
  let rec ScoreTypeToInt scoreType =
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
    | LoseTroop stars -> ScoreTypeToInt(TroopSpawned(1u, stars))
    | ParkDestroyed -> -200
    | MonumentsDestroyed -> -400
    | LoseCity cityLevel -> -(ScoreTypeToInt VillageConquered + ScoreTypeToInt(CityLevelUp cityLevel))
