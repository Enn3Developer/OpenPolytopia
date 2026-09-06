namespace OpenPolytopia.Common;

/// <summary>A participant independent of its human or future bot controller.</summary>
public interface IPlayer {
  /// <summary>The participant's tribe.</summary>
  TribeType Tribe { get; }
  /// <summary>The participant's id within a game.</summary>
  int Id { get; }
}

/// <summary>A human participant in a game.</summary>
public record Player(TribeType Tribe, int Id) : IPlayer;
