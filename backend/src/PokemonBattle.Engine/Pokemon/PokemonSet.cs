using PokemonBattle.Engine.Moves;

namespace PokemonBattle.Engine.Pokemon;

/// <summary>A team member as built by a player before the battle starts.</summary>
/// <param name="WeightHg">Weight in hectograms (Low Kick, Heavy Slam...); never below 1 in battle.</param>
/// <param name="Happiness">Friendship from 0 to 255 (Return, Frustration). Showdown defaults to 255.</param>
/// <param name="AbilityBreakable">Mold Breaker can ignore the ability (Showdown's "breakable" flag).</param>
/// <param name="AbilityTraceable">Trace can copy the ability (false for Showdown's "notrace" flag).</param>
/// <param name="BaseSpecies">
/// Base species of a forme (Pikachu for Pikachu-Cosplay); null means <paramref name="Species"/> itself.
/// Species-specific items (Light Ball, Thick Club, Stick) check it.
/// </param>
/// <param name="CanEvolve">The species can still evolve (Showdown's "nfe"); Eviolite reads it.</param>
/// <param name="PlateType">Type of the held Plate (the catalog's "onPlate"), which it boosts; null for other items.</param>
/// <param name="ItemData">
/// Battle data of <paramref name="Item"/> (Fling, Natural Gift, Mega Stone binding, Plate type),
/// which moves with the item in battle. Null gives the item only its id and <paramref name="PlateType"/>.
/// </param>
/// <param name="Mega">
/// The Mega Evolution its held Mega Stone allows (Showdown's canMegaEvo); it is only available while
/// the set holds <see cref="MegaForme.Item"/> and the stone is bound to the set's base species.
/// </param>
/// <param name="SpeciesAbilities">
/// Ids of every ability the species can have (Showdown's species.abilities). Foes read it to mark a
/// slot as maybe trapped without revealing which ability this Pokémon really has. Null means only
/// <paramref name="Ability"/>.
/// </param>
public sealed record PokemonSet(
    string Species,
    IReadOnlyList<PokemonType> Types,
    int Level,
    Stats Stats,
    IReadOnlyList<MoveDefinition> Moves,
    string? Ability = null,
    string? Item = null,
    int WeightHg = 1,
    int Happiness = 255,
    bool AbilityBreakable = false,
    bool AbilityTraceable = true,
    string? BaseSpecies = null,
    bool CanEvolve = false,
    PokemonType? PlateType = null,
    ItemDefinition? ItemData = null,
    MegaForme? Mega = null,
    IReadOnlyList<string>? SpeciesAbilities = null)
{
    public const int MaxMoves = 4;
}
