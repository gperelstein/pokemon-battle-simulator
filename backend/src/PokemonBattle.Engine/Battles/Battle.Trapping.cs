using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Trapping abilities, following Showdown's data/abilities.ts (Gen 6) and sim/pokemon.ts: a foe of
/// an Arena Trap holder that is grounded, a Steel-type foe of a Magnet Pull holder, or any foe of a
/// Shadow Tag holder that does not have Shadow Tag itself cannot choose to switch. Ghost types are
/// immune to trapping (their "trapped" type immunity). Self-switch moves still work. A Shed Shell
/// holder can always switch (data/items.ts shedshell: onTrapPokemon, priority -10, clears trapped).
/// <para>
/// The trap stays hidden (Showdown's tryTrap(true) and maybeTrapped): a turn request marks a slot as
/// maybe trapped when an adjacent foe's current ability, or any other ability its current species can
/// have (sim/battle.ts FoeMaybeTrapPokemon, Gen 3+), would trap it, and only a rejected switch
/// reveals it (sim/side.ts chooseSwitch). Deliberate divergence: a Shed Shell holder is never marked,
/// while Showdown still marks it for the foe's other possible abilities and so leaks the real one.
/// </para>
/// </summary>
public sealed partial class Battle
{
    private const string TrappedError = "Can't switch: the active Pokémon is trapped.";

    private bool IsTrapped(BattlePokemon pokemon) => CanBeTrapped(pokemon) && FoesOf(pokemon).Any(foe => Traps(foe.Ability, pokemon));

    private bool IsMaybeTrapped(BattlePokemon pokemon) =>
        CanBeTrapped(pokemon) && FoesOf(pokemon).Any(foe =>
            Traps(foe.Ability, pokemon) || (foe.Set.SpeciesAbilities ?? []).Any(ability => Traps(ability, pokemon)));

    private bool CanBeTrapped(BattlePokemon pokemon) =>
        pokemon.Item != HeldItems.ShedShell && !_typeChart.IsImmune(pokemon.Set.Types, TypeCondition.Trapped);

    private static bool Traps(string? ability, BattlePokemon pokemon) => ability switch
    {
        "arenatrap" => IsGrounded(pokemon),
        "magnetpull" => pokemon.Set.Types.Contains(PokemonType.Steel),
        "shadowtag" => pokemon.Ability != "shadowtag",
        _ => false,
    };

    /// <summary>
    /// A rejected switch from a really trapped slot reveals it: the pending request moves the first
    /// such slot of the decision from maybe trapped to trapped (sim/side.ts emitChoiceError update).
    /// </summary>
    private void RevealTrap(DecisionRequest request, Decision decision)
    {
        var side = _sides[request.Side];
        var slot = decision.Choices.OfType<SwitchChoice>()
            .Select(choice => choice.Slot)
            .First(slot => request.TrappedSlots.Contains(slot) || IsTrapped(side.Active[slot]!));
        if (request.TrappedSlots.Contains(slot))
        {
            return;
        }

        var revealed = request with
        {
            TrappedSlots = [.. request.TrappedSlots, slot],
            MaybeTrappedSlots = request.MaybeTrappedSlots.Where(s => s != slot).ToArray(),
        };
        _pending[_pending.IndexOf(request)] = revealed;
    }

    /// <summary>
    /// On the ground: holding an Iron Ball (checked first, as in Showdown's isGrounded), or neither a
    /// Flying type, a Levitate holder nor an Air Balloon holder.
    /// </summary>
    private static bool IsGrounded(BattlePokemon pokemon) =>
        pokemon.Item == HeldItems.IronBall ||
        (!pokemon.Set.Types.Contains(PokemonType.Flying) && pokemon.Ability != "levitate" &&
            pokemon.Item != HeldItems.AirBalloon);
}
