using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Moves;

public enum MoveCategory
{
    Physical,
    Special,
    Status,
}

public enum MoveTarget
{
    /// <summary>One adjacent opposing Pokémon chosen by the player.</summary>
    AdjacentFoe,

    /// <summary>Every adjacent opposing Pokémon (spread move).</summary>
    AllAdjacentFoes,
}

/// <summary>
/// Static move data. <see cref="SelfSwitch"/> marks moves such as U-turn or Volt Switch that
/// force the user's player to pick a replacement in the middle of the turn.
/// </summary>
public sealed record MoveDefinition(
    string Id,
    string Name,
    PokemonType Type,
    MoveCategory Category,
    int BasePower,
    int Priority = 0,
    MoveTarget Target = MoveTarget.AdjacentFoe,
    bool SelfSwitch = false);
