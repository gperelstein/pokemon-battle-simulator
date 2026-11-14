using PokemonBattle.Engine.Moves;

namespace PokemonBattle.Engine.Pokemon;

/// <summary>A team member as built by a player before the battle starts.</summary>
public sealed record PokemonSet(
    string Species,
    IReadOnlyList<PokemonType> Types,
    int Level,
    Stats Stats,
    IReadOnlyList<MoveDefinition> Moves)
{
    public const int MaxMoves = 4;
}
