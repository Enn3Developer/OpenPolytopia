namespace OpenPolytopia;

using System;

/// <summary>
/// Utility class to calculate damage for combat
/// </summary>
public class DamageCalculator {
  /// <summary>
  /// Computes the melee combat between two units
  /// </summary>
  /// <param name="attack">attacker attack</param>
  /// <param name="hp">attacker current hp</param>
  /// <param name="maxHp">attacker max hp</param>
  /// <param name="enemyDefense">defender defense</param>
  /// <param name="enemyHp">defender current hp</param>
  /// <param name="enemyMaxHp">defender max hp</param>
  /// <param name="attackDamage">return value of the attack damage</param>
  /// <param name="defenseDamage">return value of the retaliation damage by defender</param>
  /// <param name="enemyDefenseBonus">defender defense bonus</param>
  public static void ComputeMeleeDamage(uint attack, uint hp, uint maxHp, uint enemyDefense, uint enemyHp,
    uint enemyMaxHp, out uint attackDamage, out uint defenseDamage, float enemyDefenseBonus = 1.0f) {
    var attackForce = attack * ((float)hp / maxHp);
    var defenseForce = enemyDefense * ((float)enemyHp / enemyMaxHp) * enemyDefenseBonus;
    var totalDamage = attackForce + defenseForce;
    attackDamage = (uint)MathF.Round((attackForce / totalDamage) * attack * 4.5f);
    defenseDamage = (uint)MathF.Round((defenseForce / totalDamage) * defenseForce * 4.5f);
  }

  /// <summary>
  /// Computes the ranged damage
  /// </summary>
  /// <param name="attack">attacker attack</param>
  /// <param name="hp">attacker current hp</param>
  /// <param name="maxHp">attacker max hp</param>
  /// <param name="enemyDefense">defender defense</param>
  /// <param name="enemyHp">defender current hp</param>
  /// <param name="enemyMaxHp">defender max hp</param>
  /// <param name="enemyDefenseBonus">defender defense bonus</param>
  /// <returns>the ranged attack damage</returns>
  public static uint ComputeRangedDamage(uint attack, uint hp, uint maxHp, uint enemyDefense, uint enemyHp,
    uint enemyMaxHp, float enemyDefenseBonus = 1.0f) {
    ComputeMeleeDamage(attack, hp, maxHp, enemyDefense, enemyHp, enemyMaxHp, out var damage, out _,
      enemyDefenseBonus);
    return damage;
  }

  /// <summary>
  /// Computes the splash damage
  /// </summary>
  /// <param name="attack">attacker attack</param>
  /// <param name="hp">attacker current hp</param>
  /// <param name="maxHp">attacker max hp</param>
  /// <param name="enemyDefense">defender defense</param>
  /// <param name="enemyHp">defender current hp</param>
  /// <param name="enemyMaxHp">defender max hp</param>
  /// <param name="enemyDefenseBonus">defender defense bonus</param>
  /// <returns>the splash attack damage</returns>
  public static uint ComputeSplashDamage(uint attack, uint hp, uint maxHp, uint enemyDefense, uint enemyHp,
    uint enemyMaxHp, float enemyDefenseBonus = 1.0f) =>
    ComputeRangedDamage(attack, hp, maxHp, enemyDefense, enemyHp, enemyMaxHp, enemyDefenseBonus) / 2;
}
