namespace OpenPolytopia.Data;

using System;
using System.Linq;
using Common;
using Godot;
using Godot.Collections;

/// <summary>
/// The content of <c>tribes.json</c>, editable from the inspector
/// </summary>
[Tool]
[GlobalClass]
public partial class TribesResource : Resource {
  /// <summary>
  /// Every tribe of the game
  /// </summary>
  [Export]
  public Array<TribeResource> Tribes { get; set; } = new();

  /// <summary>
  /// Builds the resource from the deserialized json
  /// </summary>
  /// <param name="data">the tribes data</param>
  public static TribesResource FromData(TribesSerializedData data) {
    ArgumentNullException.ThrowIfNull(data);
    ArgumentNullException.ThrowIfNull(data.Tribes);

    return new TribesResource {
      Tribes = [.. data.Tribes.Select(tribe => TribeResource.FromData(tribe.TribeType, tribe.Tribe))]
    };
  }

  /// <summary>
  /// Converts the resource back to the json data
  /// </summary>
  /// <exception cref="InvalidOperationException">threw when an element of <see cref="Tribes"/> is empty</exception>
  public TribesSerializedData ToData() => new() {
    Tribes = Tribes.Select((tribe, i) =>
      (tribe ?? throw new InvalidOperationException($"tribe {i} is empty")).ToData()).ToList()
  };
}
