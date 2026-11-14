using PokemonBattle.Engine.Moves;

namespace PokemonBattle.Engine.Pokemon;

/// <summary>A team member as built by a player before the battle starts.</summary>
/// <param name="WeightHg">Weight in hectograms (Low Kick, Heavy Slam...); never below 1 in battle.</param>
/// <param name="Happiness">Friendship from 0 to 255 (Return, Frustration). Showdown defaults to 255.</param>
/// <param name="AbilityBreakable">Mold Breaker can ignore the ability (Showdown's "breakable" flag).</param>
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
    bool AbilityBreakable = false)
{
    public const int MaxMoves = 4;
}
