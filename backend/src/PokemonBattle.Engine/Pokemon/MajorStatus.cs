namespace PokemonBattle.Engine.Pokemon;

/// <summary>Non-volatile status: a Pokémon has at most one, and it persists when switching out.</summary>
public enum MajorStatus
{
    Burn,
    Paralysis,
    Poison,

    /// <summary>Badly poisoned: damage grows by 1/16 of max HP every turn on the field.</summary>
    Toxic,
    Sleep,
    Freeze,
}
