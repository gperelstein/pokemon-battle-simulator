using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Trapping abilities, following Showdown's data/abilities.ts (Gen 6) and sim/pokemon.ts: a foe of
/// an Arena Trap holder that is grounded, a Steel-type foe of a Magnet Pull holder, or any foe of a
/// Shadow Tag holder that does not have Shadow Tag itself cannot choose to switch. Ghost types are
/// immune to trapping (their "trapped" type immunity). Self-switch moves still work.
/// </summary>
public sealed partial class Battle
{
    private bool IsTrapped(BattlePokemon pokemon)
    {
        if (_typeChart.IsImmune(pokemon.Set.Types, TypeCondition.Trapped))
        {
            return false;
        }

        return FoesOf(pokemon).Any(foe => foe.Ability switch
        {
            "arenatrap" => IsGrounded(pokemon),
            "magnetpull" => pokemon.Set.Types.Contains(PokemonType.Steel),
            "shadowtag" => pokemon.Ability != "shadowtag",
            _ => false,
        });
    }

    /// <summary>
    /// On the ground: holding an Iron Ball (checked first, as in Showdown's isGrounded), or neither a
    /// Flying type nor a Levitate holder.
    /// </summary>
    private static bool IsGrounded(BattlePokemon pokemon) =>
        pokemon.Item == HeldItems.IronBall ||
        (!pokemon.Set.Types.Contains(PokemonType.Flying) && pokemon.Ability != "levitate");
}
