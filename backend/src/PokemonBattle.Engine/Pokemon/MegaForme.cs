namespace PokemonBattle.Engine.Pokemon;

/// <summary>
/// The Mega Evolution a set can reach in battle by holding its Mega Stone (Showdown's
/// item.megaStone), with its stats already calculated from the set's level, IVs, EVs and nature.
/// </summary>
/// <param name="Species">Forme name, e.g. "Charizard-Mega-Y".</param>
/// <param name="Item">Id of the Mega Stone that triggers it.</param>
/// <param name="Stats">Stats of the forme; the Pokémon keeps its HP stat (every Gen 6 Mega has its base species' base HP).</param>
/// <param name="Ability">Ability id of the forme.</param>
/// <param name="WeightHg">Weight of the forme in hectograms.</param>
/// <param name="AbilityBreakable">Mold Breaker can ignore the forme's ability.</param>
/// <param name="AbilityTraceable">Trace can copy the forme's ability.</param>
public sealed record MegaForme(
    string Species,
    string Item,
    IReadOnlyList<PokemonType> Types,
    Stats Stats,
    string Ability,
    int WeightHg,
    bool AbilityBreakable = false,
    bool AbilityTraceable = true);
