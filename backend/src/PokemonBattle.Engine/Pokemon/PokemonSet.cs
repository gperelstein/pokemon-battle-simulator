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
    PokemonType? PlateType = null)
{
    public const int MaxMoves = 4;
}
