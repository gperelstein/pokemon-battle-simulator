namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Mega Evolution, following Showdown's sim/battle-actions.ts runMegaEvo and sim/pokemon.ts
/// formeChange (Gen 6 uses them unchanged: no gen5-gen8 mod overrides them):
/// <list type="bullet">
/// <item>A player asks for it with a move choice (<see cref="MoveChoice.MegaEvolve"/>) for a slot in
/// <see cref="DecisionRequest.MegaSlots"/>; only one per side and per battle (sim/side.ts chooseMove).</item>
/// <item>It runs as its own action after switches and before moves (sim/battle-queue.ts megaEvo order
/// 104), ordered by speed; the turn keeps the speed it was ordered with (only Gen 7+ re-sorts).</item>
/// <item>The forme's species, types, stats, weight and ability apply at once and stay for the rest of
/// the battle, through switching out and fainting; the new ability runs its start effect (setAbility
/// calls the ability's Start, e.g. Drought, Trace, Mold Breaker's announcement), then White Herb's
/// onAnyAfterMega and the Update pass (Insomnia curing sleep) follow.</item>
/// </list>
/// </summary>
public sealed partial class Battle
{
    private void RunMegaEvolution(Side side, int slot, BattlePokemon pokemon)
    {
        if (!pokemon.CanMegaEvolve)
        {
            return;
        }

        var before = pokemon.MegaEvolve();
        _events.Add(new MegaEvolved(side.Id, slot, before, pokemon.Set.Species));

        // Limit one Mega Evolution per side (runMegaEvo clears canMegaEvo for every team member).
        foreach (var teammate in side.Team)
        {
            teammate.CanMegaEvolve = false;
        }

        StartAbility(pokemon);
        RestoreLoweredStagesOfActivePokemon();
    }
}
