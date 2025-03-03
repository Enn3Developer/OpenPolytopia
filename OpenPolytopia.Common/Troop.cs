namespace OpenPolytopia.Common;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

/// <summary>
/// Manages the troops
/// </summary>
/// <param name="size">size of the width of the grid</param>
public class TroopManager(uint size) {
  private readonly Dictionary<TroopType, Troop> _troops = new(16);
  private readonly TroopData[] _grid = new TroopData[size * size];

  /// <summary>
  /// Gets the troop data
  /// </summary>
  /// <param name="position">the position of the troop</param>
  /// <remarks>
  /// Remember to check if the troop is valid using <see cref="TroopData.IsValid"/>
  /// </remarks>
  public TroopData this[Vector2I position] {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _grid[GridPositionToIndex(position)];
  }

  /// <summary>
  /// Gets the troop data
  /// </summary>
  /// <param name="index">the index of the troop</param>
  /// <remarks>
  /// Remember to check if the troop is valid using <see cref="TroopData.IsValid"/>
  /// </remarks>
  public TroopData this[uint index] {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _grid[index];
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public uint GridPositionToIndex(Vector2I position) => (uint)((position.Y * size) + position.X);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public uint GridPositionToIndex(int x, int y) => (uint)((y * size) + x);

  /// <summary>
  /// Registers a new type of troop
  /// </summary>
  /// <param name="type">the troop type</param>
  /// <typeparam name="T">the troop class that extends <see cref="Troop"/></typeparam>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void RegisterTroop<T>(TroopType type) where T : Troop, new() => _troops.Add(type, new T());

  /// <summary>
  /// Spawns a new troop
  /// </summary>
  /// <param name="position">where to spawn the troop in the grid</param>
  /// <param name="player">the player owning the troop</param>
  /// <param name="city">the troop's city; can be 0 if the troop has <see cref="Skill.Independent"/></param>
  /// <param name="type">the type of the troop</param>
  /// <exception cref="ArgumentException">when there already is a troop on that tile</exception>
  /// <exception cref="KeyNotFoundException">when the type of the troop passed was not registered beforehand</exception>
  public void SpawnTroop(Vector2I position, uint player, uint city, TroopType type) {
    var index = GridPositionToIndex(position);
    if (_grid[index].IsValid()) {
      throw new ArgumentException("position argument isn't valid because there already is a troop there",
        nameof(position));
    }

    if (!_troops.TryGetValue(type, out var troop)) {
      throw new KeyNotFoundException($"can't find key for type {type}");
    }

    var troopData = new TroopData { Type = type, Hp = troop.MaxHp, Player = player, City = city };
    _grid[index] = troopData;
  }

  /// <summary>
  /// Deletes a troop from the grid
  /// </summary>
  /// <param name="position">position of the troop to delete</param>
  /// <returns>the troop that was deleted</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public TroopData DeleteTroop(Vector2I position) {
    var index = GridPositionToIndex(position);
    var troop = _grid[index];
    _grid[index].Delete();
    return troop;
  }

  /// <summary>
  /// Moves a troop from a tile to another
  /// </summary>
  /// <remarks>
  /// This method doesn't check if the troop already moved and doesn't check if the troop can effectively move there,
  /// this work should be done by the caller
  /// </remarks>
  /// <param name="originalPosition">original position of the troop</param>
  /// <param name="newPosition">new position of the troop</param>
  /// <exception cref="ArgumentException">when there already is a troop on that tile</exception>
  public void MoveTroop(Vector2I originalPosition, Vector2I newPosition) {
    var newIndex = GridPositionToIndex(newPosition);
    if (_grid[newIndex].IsValid()) {
      throw new ArgumentException("newPosition argument isn't valid because there already is a troop there",
        nameof(newPosition));
    }

    var troop = DeleteTroop(originalPosition);
    troop.Moved = true;
    _grid[newIndex] = troop;
  }

  /// <summary>
  /// Sets the troop to be a veteran, thus resetting his hp, adding +5 to his max hp
  /// </summary>
  /// <param name="position">the position of the troop</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetVeteran(Vector2I position) {
    var index = GridPositionToIndex(position);
    if (!_grid[index].IsValid()) {
      return;
    }

    _grid[index].Veteran = true;
    _grid[index].Hp = _troops[_grid[index].Type].MaxHp + 5;
  }

  /// <summary>
  /// Modifies the troop
  /// </summary>
  /// <param name="position">the position of the troop</param>
  /// <param name="callback">function callback used to modify the troop</param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void ModifyTroop(Vector2I position, ActionRef<TroopData> callback) =>
    callback(ref _grid[GridPositionToIndex(position)]);
}

public struct TroopData {
  private const int VETERAN_POSITION = 31;
  private const int ATTACKED_POSITION = 30;
  private const int MOVED_POSITION = 29;
  private const int CITY_POSITION = 21;
  private const int HP_POSITION = 13;
  private const int TYPE_POSITION = 5;
  private const int PLAYER_POSITION = 4;

  private const int ONE_BIT = 1;
  private const int FOUR_BITS = 15;
  private const int EIGHT_BITS = 255;

  /// <summary>
  /// Inner representation of the troop data.
  ///
  /// Using 0 as the left-most bit and 63 as the right-most bit
  /// <list type="bullet">
  /// <item>0 -> is veteran</item>
  /// <item>1 -> has attacked</item>
  /// <item>2 -> has moved</item>
  /// <item>[3, 10] -> City of spawn</item>
  /// <item>[11, 18] -> current hp</item>
  /// <item>[19, 26] -> troop type</item>
  /// <item>[27, 30] -> player</item>
  /// </list>
  /// </summary>
  private uint _inner;

  /// <summary>
  /// Whether this troop is a veteran
  /// </summary>
  public bool Veteran {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(ONE_BIT, VETERAN_POSITION) == 1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value.ToUInt(), ONE_BIT, VETERAN_POSITION);
  }

  /// <summary>
  /// Whether this troop has attacked
  /// </summary>
  /// <remarks>
  /// Remember to reset this value every turn; you can use <see cref="TroopData.ResetActions"/>
  /// </remarks>
  public bool Attacked {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(ONE_BIT, ATTACKED_POSITION) == 1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value.ToUInt(), ONE_BIT, ATTACKED_POSITION);
  }

  /// <summary>
  /// Whether this troop has moved
  /// </summary>
  /// <remarks>
  /// Remember to reset this value every turn; you can use <see cref="TroopData.ResetActions"/>
  /// </remarks>
  public bool Moved {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(ONE_BIT, MOVED_POSITION) == 1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value.ToUInt(), ONE_BIT, MOVED_POSITION);
  }

  /// <summary>
  /// The city this troop comes from
  /// </summary>
  public uint City {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(EIGHT_BITS, CITY_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value, EIGHT_BITS, CITY_POSITION);
  }

  /// <summary>
  /// Current hp of the troop
  /// </summary>
  public uint Hp {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(EIGHT_BITS, HP_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value, EIGHT_BITS, HP_POSITION);
  }

  /// <summary>
  /// The type of the troop
  /// </summary>
  public TroopType Type {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => (TroopType)_inner.GetBits(EIGHT_BITS, TYPE_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits((uint)value, EIGHT_BITS, TYPE_POSITION);
  }

  /// <summary>
  /// The player who owns this troop
  /// </summary>
  public uint Player {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => _inner.GetBits(FOUR_BITS, PLAYER_POSITION);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => _inner.SetBits(value, FOUR_BITS, PLAYER_POSITION);
  }

  /// <summary>
  /// Reset the possible actions a troop can do
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void ResetActions() {
    Attacked = false;
    Moved = false;
  }

  /// <summary>
  /// Checks if this data is valid or not
  /// </summary>
  /// <returns>true if valid; false otherwise</returns>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool IsValid() => _inner != 0;

  /// <summary>
  /// Sets the data to an invalid state thus deleting it
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Delete() => _inner = 0;
}

public abstract class Troop {
  /// <summary>
  /// Max hp of the troop
  /// </summary>
  public abstract uint MaxHp { get; }

  /// <summary>
  /// Attack of the troop
  /// </summary>
  public abstract uint Attack { get; }

  /// <summary>
  /// Defense of the troop
  /// </summary>
  public abstract uint Defense { get; }

  /// <summary>
  /// Movement of the troop in tiles
  /// </summary>
  public abstract uint Movement { get; }

  /// <summary>
  /// Attack range of the troop in tiles
  /// </summary>
  public abstract uint Range { get; }

  /// <summary>
  /// List of skills this troop has
  /// </summary>
  public abstract Skill[] Skills { get; }
}

/// <summary>
/// Possible skills that troops can have
/// </summary>
public enum Skill {
  Amphibious,
  Carry,
  Convert,
  Dash,
  Escape,
  Floating,
  Fly,
  Fortify,
  Grow,
  Heal,
  Hide,
  Independent,
  Infiltrate,
  Navigate,
  Persist,
  Static,
  Scout,
  Sneak,
  Splash,
  Stiff,
  Stomp,
  Surprise,
  Water
}

public enum TroopType {
  Warrior,
  Archer
}

#region Troop implementations

public class WarriorTroop : Troop {
  public override uint MaxHp => 10;
  public override uint Attack => 2;
  public override uint Defense => 2;
  public override uint Movement => 1;
  public override uint Range => 1;
  public override Skill[] Skills => [Skill.Dash, Skill.Fortify];
}

#endregion
