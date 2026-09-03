namespace OpenPolytopia.Common.Gameplay;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Movement, combat and training actions
/// </summary>
public partial class Game {
  /// <summary>
  /// Moves a troop along a reachable path to another tile
  /// </summary>
  /// <param name="playerId">the player owning the troop</param>
  /// <param name="from">the troop's current position</param>
  /// <param name="to">the tile to move it to</param>
  /// <returns>
  /// <see cref="GameActionResult.Unreachable"/> if <paramref name="to"/> isn't in the troop's reachable tiles
  /// (see <see cref="TroopMovement.ReachableTiles"/>), the usual turn/ownership/action checks, or
  /// <see cref="GameActionResult.Ok"/> on success
  /// </returns>
  public GameActionResult MoveTroop(int playerId, Vector2I from, Vector2I to) {
    var turnResult = CheckTurn(playerId, out var player);
    if (turnResult != GameActionResult.Ok) {
      return turnResult;
    }

    if (!IsInside(from) || !IsInside(to)) {
      return GameActionResult.InvalidPosition;
    }

    var troop = Troops[from];
    if (!troop.IsValid()) {
      return GameActionResult.NoTroop;
    }

    if (troop.Player != (uint)playerId) {
      return GameActionResult.NotTroopOwner;
    }

    if (troop.Moved) {
      return GameActionResult.TroopAlreadyMoved;
    }

    var troopDef = Troops[troop.Type];
    if (troop.Attacked && !troopDef.Skills.HasSkill(Skill.Escape)) {
      return GameActionResult.TroopAlreadyAttacked;
    }

    var canClimb = player.TechTree.HasResearched(TechIds.CLIMBING);
    var reachable = new TroopMovement(Troops, Grid).ReachableTiles(from, canClimb);
    if (!reachable.Contains(to)) {
      return GameActionResult.Unreachable;
    }

    Troops.MoveTroop(from, to);
    return GameActionResult.Ok;
  }

  /// <summary>
  /// Attacks an enemy troop within range
  /// </summary>
  /// <param name="playerId">the player owning the attacking troop</param>
  /// <param name="from">the attacking troop's position</param>
  /// <param name="target">the position of the troop to attack</param>
  /// <returns>the outcome of the attack, see <see cref="AttackResult"/></returns>
  public AttackResult Attack(int playerId, Vector2I from, Vector2I target) {
    var turnResult = CheckTurn(playerId, out _);
    if (turnResult != GameActionResult.Ok) {
      return RejectedAttack(turnResult);
    }

    if (!IsInside(from) || !IsInside(target)) {
      return RejectedAttack(GameActionResult.InvalidPosition);
    }

    var attacker = Troops[from];
    if (!attacker.IsValid()) {
      return RejectedAttack(GameActionResult.NoTroop);
    }

    if (attacker.Player != (uint)playerId) {
      return RejectedAttack(GameActionResult.NotTroopOwner);
    }

    if (attacker.Attacked) {
      return RejectedAttack(GameActionResult.TroopAlreadyAttacked);
    }

    var attackerDef = Troops[attacker.Type];
    if (attacker.Moved && !attackerDef.Skills.HasSkill(Skill.Dash)) {
      return RejectedAttack(GameActionResult.TroopAlreadyMoved);
    }

    if (attackerDef.Attack == 0) {
      return RejectedAttack(GameActionResult.CannotAttack);
    }

    var defender = Troops[target];
    if (!defender.IsValid()) {
      return RejectedAttack(GameActionResult.NoTarget);
    }

    if (defender.Player == attacker.Player) {
      return RejectedAttack(GameActionResult.NotEnemy);
    }

    var distance = ChebyshevDistance(from, target);
    if (distance > attackerDef.Range) {
      return RejectedAttack(GameActionResult.OutOfRange);
    }

    return ResolveAttack(from, target, attacker, attackerDef, defender, distance);
  }

  /// <summary>
  /// Trains a new troop in one of the player's cities
  /// </summary>
  /// <param name="playerId">the player training the troop</param>
  /// <param name="city">the position of the city tile to train in</param>
  /// <param name="type">the type of troop to train</param>
  /// <returns>
  /// <see cref="GameActionResult.TroopNotTrainable"/> if the type has no <see cref="Troop.Cost"/>,
  /// <see cref="GameActionResult.CityFull"/> if the city already has <c>Level + 1</c> troops, the usual
  /// turn/ownership/tech/star checks, or <see cref="GameActionResult.Ok"/> on success
  /// </returns>
  public GameActionResult TrainTroop(int playerId, Vector2I city, TroopType type) {
    var turnResult = CheckTurn(playerId, out var player);
    if (turnResult != GameActionResult.Ok) {
      return turnResult;
    }

    if (!IsInside(city)) {
      return GameActionResult.InvalidPosition;
    }

    var tile = Grid[city];
    if (tile.Kind != TileKind.Village || tile.City == 0) {
      return GameActionResult.NotACity;
    }

    var cityId = (uint)tile.City;
    var cityData = Cities[cityId];
    if (cityData.Owner != playerId) {
      return GameActionResult.CityNotOwned;
    }

    if (Troops[city].IsValid()) {
      return GameActionResult.TileOccupied;
    }

    if (!Troops.IsRegistered(type)) {
      return GameActionResult.InvalidTroopType;
    }

    var troopDef = Troops[type];
    if (troopDef.Cost == 0) {
      return GameActionResult.TroopNotTrainable;
    }

    if (troopDef.RequiredTech != null && !player.TechTree.HasResearched(troopDef.RequiredTech)) {
      return GameActionResult.TechLocked;
    }

    if (cityData.Troops >= cityData.Level + 1) {
      return GameActionResult.CityFull;
    }

    if (player.Stars < (int)troopDef.Cost) {
      return GameActionResult.NotEnoughStars;
    }

    player.Stars -= (int)troopDef.Cost;
    Troops.SpawnTroop(city, (uint)playerId, cityId, type);
    // a freshly trained troop can't act the turn it's trained
    Troops.ModifyTroop(city, (ref TroopData data) => {
      data.Moved = true;
      data.Attacked = true;
    });
    Cities.ModifyCity(cityId, (ref CityData c) => c.Troops++);
    player.Score.AddScore(ScoreType.TroopSpawned(1, troopDef.Cost));

    return GameActionResult.Ok;
  }

  /// <summary>
  /// Every tile a player's troop can end its movement on
  /// </summary>
  /// <param name="playerId">the player owning the troop</param>
  /// <param name="from">the troop's position</param>
  /// <returns>the reachable tiles; empty if there's no troop of that player at <paramref name="from"/></returns>
  public HashSet<Vector2I> ReachableTiles(int playerId, Vector2I from) {
    if (!IsInside(from)) {
      return [];
    }

    var troop = Troops[from];
    if (!troop.IsValid() || troop.Player != (uint)playerId) {
      return [];
    }

    var player = this[playerId];
    if (player == null) {
      return [];
    }

    var canClimb = player.TechTree.HasResearched(TechIds.CLIMBING);
    return new TroopMovement(Troops, Grid).ReachableTiles(from, canClimb);
  }

  /// <summary>
  /// The max hp of a troop, accounting for whether it's a veteran
  /// </summary>
  /// <param name="troop">the troop</param>
  /// <returns><see cref="Troop.MaxHp"/>, plus 5 if <see cref="TroopData.Veteran"/></returns>
  public uint MaxHpOf(TroopData troop) => Troops[troop.Type].MaxHp + (troop.Veteran ? 5u : 0u);

  /// <summary>
  /// Resolves a legal attack: damage, retaliation, kills, and the attacker moving in on a melee kill
  /// </summary>
  /// <param name="from">the attacker's position before the attack</param>
  /// <param name="target">the defender's position</param>
  /// <param name="attacker">the attacker's data</param>
  /// <param name="attackerDef">the attacker's troop definition</param>
  /// <param name="defender">the defender's data</param>
  /// <param name="distance">the Chebyshev distance between attacker and defender</param>
  /// <returns>the resolved <see cref="AttackResult"/></returns>
  private AttackResult ResolveAttack(Vector2I from, Vector2I target, TroopData attacker, Troop attackerDef,
    TroopData defender, int distance) {
    var defenderDef = Troops[defender.Type];
    var defenderBonus = DefenseBonus(target, defender, defenderDef);

    DamageCalculator.ComputeMeleeDamage(attackerDef.Attack, attacker.Hp, MaxHpOf(attacker), defenderDef.Defense,
      defender.Hp, MaxHpOf(defender), out var attackDamage, out var defenseDamage, defenderBonus);

    var defenderHp = attackDamage >= defender.Hp ? 0u : defender.Hp - attackDamage;
    var defenderKilled = defenderHp == 0;
    var attackerHp = attacker.Hp;
    var attackerKilled = false;

    if (defenderKilled) {
      KillTroop(Grid.GridPositionToIndex(target));
    }
    else {
      Troops.ModifyTroop(target, (ref TroopData data) => data.Hp = defenderHp);

      // retaliation: only a surviving defender that can reach back and can actually attack retaliates
      if (distance <= defenderDef.Range && defenderDef.Attack > 0) {
        attackerHp = defenseDamage >= attacker.Hp ? 0u : attacker.Hp - defenseDamage;
        attackerKilled = attackerHp == 0;
        if (attackerKilled) {
          KillTroop(Grid.GridPositionToIndex(from));
        }
        else {
          Troops.ModifyTroop(from, (ref TroopData data) => data.Hp = attackerHp);
        }
      }
    }

    var attackerPosition = from;
    if (!attackerKilled) {
      // a melee kill (distance 1) pulls the attacker onto the vacated tile, unless it's Stiff; this doesn't count
      // as a move, so the troop's Moved flag from before the attack is carried over unchanged
      if (defenderKilled && distance == 1 && !attackerDef.Skills.HasSkill(Skill.Stiff)) {
        Troops.MoveTroop(from, target);
        var wasMoved = attacker.Moved;
        Troops.ModifyTroop(target, (ref TroopData data) => data.Moved = wasMoved);
        attackerPosition = target;
      }

      // Persist lets the attacker attack again after a kill, so Attacked stays false in that one case
      if (!(defenderKilled && attackerDef.Skills.HasSkill(Skill.Persist))) {
        Troops.ModifyTroop(attackerPosition, (ref TroopData data) => data.Attacked = true);
      }
    }

    return new AttackResult(GameActionResult.Ok, attackerKilled ? 0u : attackerHp, defenderHp, attackerKilled,
      defenderKilled, attackerPosition);
  }

  /// <summary>
  /// The defense bonus multiplier a defender gets from standing fortified in its own city
  /// </summary>
  /// <param name="position">the defender's position</param>
  /// <param name="defender">the defender's data</param>
  /// <param name="defenderDef">the defender's troop definition</param>
  /// <returns>4.0 with a wall, 1.5 fortified without one, 1.0 otherwise</returns>
  private float DefenseBonus(Vector2I position, TroopData defender, Troop defenderDef) {
    if (!defenderDef.Skills.HasSkill(Skill.Fortify)) {
      return 1.0f;
    }

    var tile = Grid[position];
    if (tile.Kind != TileKind.Village || tile.City == 0) {
      return 1.0f;
    }

    var city = Cities[(uint)tile.City];
    if (city.Owner != (int)defender.Player) {
      return 1.0f;
    }

    return city.Wall ? 4.0f : 1.5f;
  }

  /// <summary>
  /// Builds the result for an attack rejected before doing anything
  /// </summary>
  /// <param name="result">the rejection reason</param>
  /// <returns>the rejected result, with every value zeroed out</returns>
  private static AttackResult RejectedAttack(GameActionResult result) => new(result, 0, 0, false, false, default);

  /// <summary>
  /// The Chebyshev distance (8-neighborhood) between two grid positions
  /// </summary>
  /// <param name="a">the first position</param>
  /// <param name="b">the second position</param>
  /// <returns><c>max(|dx|, |dy|)</c></returns>
  private static int ChebyshevDistance(Vector2I a, Vector2I b) =>
    Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

/// <summary>
/// The outcome of <see cref="Game.Attack"/>
/// </summary>
/// <param name="Result">whether the attack was accepted</param>
/// <param name="AttackerHp">the attacker's hp after the attack; 0 if it died</param>
/// <param name="DefenderHp">the defender's hp after the attack; 0 if it died</param>
/// <param name="AttackerKilled">whether the attacker died to retaliation</param>
/// <param name="DefenderKilled">whether the defender died</param>
/// <param name="AttackerPosition">
/// the attacker's position after the attack: <c>from</c>, or the defender's tile if it moved in on a melee kill
/// </param>
public readonly record struct AttackResult(GameActionResult Result, uint AttackerHp, uint DefenderHp,
  bool AttackerKilled, bool DefenderKilled, Vector2I AttackerPosition);
