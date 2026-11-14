namespace PokemonBattle.Engine.Pokemon;

/// <summary>
/// Effects a type can be naturally immune to, besides move damage (Showdown's lowercase
/// typechart entries: brn, par, psn/tox, frz, powder, sandstorm, hail, trapped).
/// </summary>
public enum TypeCondition
{
    Burn,
    Paralysis,

    /// <summary>Both regular and bad poison.</summary>
    Poison,
    Freeze,

    /// <summary>Powder and spore moves (Gen 6+: Grass types).</summary>
    Powder,
    Sandstorm,
    Hail,
    Trapped,
}
