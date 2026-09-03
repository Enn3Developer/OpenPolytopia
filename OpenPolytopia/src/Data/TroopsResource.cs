namespace OpenPolytopia.Data;

using System;
using System.Linq;
using Common;
using Godot;
using Godot.Collections;

/// <summary>
/// The content of <c>troops.json</c>, editable from the inspector
/// </summary>
[Tool]
[GlobalClass]
public partial class TroopsResource : Resource {
  /// <summary>
  /// Every troop of the game
  /// </summary>
  [Export]
  public Array<TroopResource> Troops { get; set; } = new();

  /// <summary>
  /// Builds the resource from the deserialized json
  /// </summary>
  /// <param name="data">the troops data</param>
  public static TroopsResource FromData(TroopsSerializedData data) {
    ArgumentNullException.ThrowIfNull(data);
    ArgumentNullException.ThrowIfNull(data.Troops);

    return new TroopsResource {
      Troops = [.. data.Troops.Select(troop => TroopResource.FromData(troop.Type, troop.Troop))]
    };
  }

  /// <summary>
  /// Converts the resource back to the json data
  /// </summary>
  /// <exception cref="InvalidOperationException">threw when an element of <see cref="Troops"/> is empty</exception>
  public TroopsSerializedData ToData() => new() {
    Troops = Troops.Select((troop, i) =>
      (troop ?? throw new InvalidOperationException($"troop {i} is empty")).ToData()).ToList()
  };
}
