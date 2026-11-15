using PokemonBattle.Engine.Moves;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Held items that restrict which move a Pokémon can choose, following Showdown's data/items.ts and
/// data/conditions.ts (Gen 6):
/// <list type="bullet">
/// <item>Choice Band, Choice Specs and Choice Scarf add the choicelock volatile on the first move
/// their holder uses (onModifyMove, so not when full paralysis or sleep stopped the move); its
/// onDisableMove disables every other move until the holder leaves the field, and drops the lock when
/// the holder no longer holds a Choice item or no longer knows the move. data/mods/gen6/conditions.ts
/// removes choicelock's onBeforeMove, so in Gen 6 the lock only acts on the choice.</item>
/// <item>Assault Vest's onDisableMove disables every status move except Me First.</item>
/// </list>
/// Disabled moves are reported in <see cref="DecisionRequest.DisabledMoves"/> and rejected by
/// <see cref="Submit"/> like Showdown's sim/side.ts chooseMove. Showdown makes a Pokémon whose every
/// move is disabled use Struggle, which the engine does not have yet: until then no restriction is
/// applied in that case.
/// </summary>
public sealed partial class Battle
{
    private static bool IsChoiceItem(string? item) => item is "choiceband" or "choicespecs" or "choicescarf";

    /// <summary>The choice item's onModifyMove: the first move used while holding it becomes the locked one.</summary>
    private static void LockChoice(BattlePokemon user, MoveDefinition move)
    {
        if (IsChoiceItem(user.Item) && user.ChoiceLockedMoveId is null)
        {
            user.ChoiceLockedMoveId = move.Id;
        }
    }

    /// <summary>
    /// Indexes of the moves <paramref name="pokemon"/> cannot choose this turn (the DisableMove
    /// event), or an empty list.
    /// </summary>
    private static IReadOnlyList<int> DisabledMoves(BattlePokemon pokemon)
    {
        var moves = pokemon.Set.Moves;
        if (pokemon.ChoiceLockedMoveId is { } locked &&
            (!IsChoiceItem(pokemon.Item) || moves.All(move => move.Id != locked)))
        {
            pokemon.ChoiceLockedMoveId = null;
        }

        var disabled = Enumerable.Range(0, moves.Count)
            .Where(index =>
                (pokemon.ChoiceLockedMoveId is { } lockedMove && moves[index].Id != lockedMove) ||
                (pokemon.Item == "assaultvest" && moves[index].Category == MoveCategory.Status && moves[index].Id != "mefirst"))
            .ToArray();

        // Showdown would fall back to Struggle; without it, leave the choice free rather than block it.
        return disabled.Length == moves.Count ? [] : disabled;
    }

    private static Dictionary<int, IReadOnlyList<int>> DisabledMoves(Side side, IEnumerable<int> slots) =>
        slots
            .Select(slot => (Slot: slot, Disabled: DisabledMoves(side.Active[slot]!)))
            .Where(entry => entry.Disabled.Count > 0)
            .ToDictionary(entry => entry.Slot, entry => entry.Disabled);
}
