namespace OpenPolytopia;

using Common;
using Godot;
using Shouldly;

public class DamageCalculatorTest {
  [Fact]
  public void TestEvenMatch() {
    // two full health warriors trade 5 damage each way
    DamageCalculator.ComputeMeleeDamage(2, 10, 10, 2, 10, 10, out var attackDamage, out var defenseDamage);
    attackDamage.ShouldBe(5u);
    defenseDamage.ShouldBe(5u);
  }

  [Fact]
  public void TestWoundedAttacker() {
    // a wounded attacker hits for less and takes more in return
    DamageCalculator.ComputeMeleeDamage(2, 5, 10, 2, 10, 10, out var attackDamage, out var defenseDamage);
    attackDamage.ShouldBe(3u);
    defenseDamage.ShouldBe(6u);
  }

  [Fact]
  public void TestWoundedDefender() {
    // a wounded defender takes more and retaliates for less
    DamageCalculator.ComputeMeleeDamage(2, 10, 10, 2, 5, 10, out var attackDamage, out var defenseDamage);
    attackDamage.ShouldBe(6u);
    defenseDamage.ShouldBe(3u);
  }

  [Fact]
  public void TestDefenseBonus() {
    // the retaliation scales linearly with the defense, not with the bonus applied to it
    DamageCalculator.ComputeMeleeDamage(2, 10, 10, 2, 10, 10, out var attackDamage, out var defenseDamage, 1.5f);
    attackDamage.ShouldBe(4u);
    defenseDamage.ShouldBe(5u);

    DamageCalculator.ComputeMeleeDamage(2, 10, 10, 2, 10, 10, out attackDamage, out defenseDamage, 4.0f);
    attackDamage.ShouldBe(2u);
    defenseDamage.ShouldBe(7u);
  }

  [Fact]
  public void TestUnevenStats() {
    // a stronger attacker against a full health warrior
    DamageCalculator.ComputeMeleeDamage(3, 10, 10, 2, 10, 10, out var attackDamage, out var defenseDamage);
    attackDamage.ShouldBe(8u);
    defenseDamage.ShouldBe(4u);

    // more health doesn't change the outcome as long as both are undamaged
    DamageCalculator.ComputeMeleeDamage(3, 15, 15, 2, 10, 10, out attackDamage, out defenseDamage);
    attackDamage.ShouldBe(8u);
    defenseDamage.ShouldBe(4u);

    DamageCalculator.ComputeMeleeDamage(5, 40, 40, 2, 10, 10, out attackDamage, out defenseDamage);
    attackDamage.ShouldBe(16u);
    defenseDamage.ShouldBe(3u);
  }

  [Fact]
  public void TestRoundsHalvesUp() {
    // 5 * (2 / 4) * 4.5 rounds up to 5, banker's rounding would give 4
    DamageCalculator.ComputeMeleeDamage(2, 10, 10, 2, 10, 10, out var attackDamage, out _);
    attackDamage.ShouldBe(5u);
  }

  [Fact]
  public void TestRangedDamage() {
    // ranged attacks deal the melee attack damage without taking any retaliation
    DamageCalculator.ComputeMeleeDamage(2, 10, 10, 2, 5, 10, out var attackDamage, out _);
    DamageCalculator.ComputeRangedDamage(2, 10, 10, 2, 5, 10).ShouldBe(attackDamage);
    DamageCalculator.ComputeRangedDamage(2, 10, 10, 2, 10, 10, 1.5f).ShouldBe(4u);
  }

  [Fact]
  public void TestSplashDamage() {
    // splash is half the ranged damage, rounded down
    DamageCalculator.ComputeSplashDamage(2, 10, 10, 2, 10, 10).ShouldBe(2u);
    DamageCalculator.ComputeSplashDamage(3, 10, 10, 2, 10, 10).ShouldBe(4u);
    DamageCalculator.ComputeSplashDamage(2, 10, 10, 2, 10, 10, 1.5f).ShouldBe(2u);
  }
}
