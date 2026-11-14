using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Moves;

public enum MoveCategory
{
    Physical,
    Special,
    Status,
}

/// <summary>Gen 6 move target kinds (mirrors Pokémon Showdown's targets).</summary>
public enum MoveTarget
{
    /// <summary>One adjacent opposing Pokémon chosen by the player.</summary>
    AdjacentFoe,

    /// <summary>Every adjacent opposing Pokémon (spread move).</summary>
    AllAdjacentFoes,

    /// <summary>
    /// One adjacent Pokémon chosen by the player. Choosing an ally is not supported yet, so it
    /// currently behaves like <see cref="AdjacentFoe"/>.
    /// </summary>
    Normal,

    /// <summary>Any Pokémon chosen by the player (relevant from Triples). Behaves like <see cref="Normal"/>.</summary>
    Any,

    /// <summary>Every adjacent Pokémon, allies included (Earthquake, Surf).</summary>
    AllAdjacent,

    /// <summary>A random adjacent foe (Outrage, Thrash...).</summary>
    RandomNormal,

    /// <summary>Target decided by the move itself (Counter, Mirror Coat...). Treated as <see cref="AdjacentFoe"/>.</summary>
    Scripted,

    Self,
    AdjacentAlly,
    AdjacentAllyOrSelf,
    Allies,
    AllySide,
    AllyTeam,
    FoeSide,
    All,
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
