using PokemonBattle.Engine.Moves;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Forced switches and switch-causing items, following Showdown's sim/battle-actions.ts and
/// sim/battle.ts (no Gen 6 override in the gen6-gen8 mods for these moves and items):
/// <list type="bullet">
/// <item>Moves with forceSwitch (Roar, Whirlwind, Dragon Tail, Circle Throw) flag every target they
/// hit that is still standing, while their user is, if the target's side has a healthy benched
/// Pokémon (forceSwitch, step 6 of spreadMoveHit, after the secondaries and before DamagingHit). A
/// status move with nothing to drag in fails ("-fail" from runMoveEffects); a damaging one just
/// hits.</item>
/// <item>AfterMoveSecondary, after the Update event and before Life Orb / Shell Bell, and skipped
/// when Sheer Force removed the move's secondaries: Eject Button (priority 2) then Red Card
/// (priority 0), holders by speed. Eject Button: a damaging move from another Pokémon leaves its
/// holder standing, the holder can switch, is not being forced out and no other Eject Button
/// activated: it is used up and its holder takes over the attacker's own self-switch. Red Card: a
/// damaging move from another Pokémon leaves both standing, the attacker is on the field, can
/// switch and neither is being forced out: it is used up and the attacker is flagged.</item>
/// <item>Once the action ends (runAction): every flagged Pokémon still standing is dragged out, side
/// by side and slot by slot, for a random healthy benched ally (getRandomSwitchable), with no
/// decision asked and its switch-in abilities run at once (dragIn, switchIn with isDrag); then
/// the Pokémon with the switch flag, if still standing on the field, gets a
/// <see cref="DecisionKind.MidTurnSwitch"/> request. A Red Card drag therefore cancels the
/// attacker's U-turn.</item>
/// </list>
/// Showdown samples the drag among its own side order, which switches reorder; the engine samples
/// the healthy bench in team order. Suction Cups, Ingrain, Substitute and Emergency Exit (DragOut
/// and related handlers) are not implemented yet.
/// </summary>
public sealed partial class Battle
{
    /// <summary>Pokémon flagged to be dragged out when the current action ends (Showdown's forceSwitchFlag).</summary>
    private readonly List<BattlePokemon> _forcedOut = [];

    /// <summary>The Pokémon that must be replaced when the current action ends (Showdown's switchFlag).</summary>
    private SwitchFlag? _switchFlag;

    /// <param name="FromItem">Set by an Eject Button (switchFlag = true) rather than a self-switch move.</param>
    private sealed record SwitchFlag(BattlePokemon Pokemon, bool FromItem);

    private void ClearSwitchFlags()
    {
        _forcedOut.Clear();
        _switchFlag = null;
    }

    /// <summary>A status move that forces a switch fails against a target with nobody to drag in.</summary>
    private BattlePokemon[] WithoutFailedDrags(MoveAction action, MoveDefinition move, BattlePokemon[] hit)
    {
        if (!move.ForceSwitch || move.Category != MoveCategory.Status)
        {
            return hit;
        }

        return hit.Where(target =>
        {
            if (_sides[target.Side].Bench.Count > 0)
            {
                return true;
            }

            _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "There is no Pokémon to drag in."));
            return false;
        }).ToArray();
    }

    private void FlagForcedSwitches(BattlePokemon user, MoveDefinition move, IEnumerable<BattlePokemon> hit)
    {
        if (!move.ForceSwitch || user.IsFainted)
        {
            return;
        }

        foreach (var target in hit.Where(t => !t.IsFainted && _sides[t.Side].Bench.Count > 0 && !_forcedOut.Contains(t)))
        {
            _forcedOut.Add(target);
        }
    }

    /// <summary>
    /// Eject Button (priority 2), then Red Card and the Kee / Maranga Berries (priority 0), each by
    /// descending holder speed.
    /// </summary>
    /// <param name="heals">Present rolled its heal (Kee Berry ignores it).</param>
    private void ItemsAfterMoveSecondary(BattlePokemon user, MoveDefinition move, IReadOnlyList<BattlePokemon> hit, bool heals)
    {
        if (move.Category == MoveCategory.Status)
        {
            return;
        }

        var holders = hit.Where(target => target != user).OrderByDescending(target => target.Speed).ToArray();
        foreach (var holder in holders.Where(h => h.Item == HeldItems.EjectButton))
        {
            if (holder.IsFainted || _sides[holder.Side].Bench.Count == 0 || _forcedOut.Contains(holder) ||
                _switchFlag is { FromItem: true })
            {
                continue;
            }

            ItemConsumption(holder, HeldItems.EjectButton);
            // The holder's switch replaces the attacker's own (U-turn, Volt Switch).
            _switchFlag = new SwitchFlag(holder, FromItem: true);
        }

        foreach (var holder in holders)
        {
            if (holder.Item != HeldItems.RedCard)
            {
                DefenseBerryAfterHit(holder, move, heals);
                continue;
            }

            if (holder.IsFainted || user.IsFainted || Locate(user).Slot < 0 || _sides[user.Side].Bench.Count == 0 ||
                _forcedOut.Contains(user) || _forcedOut.Contains(holder))
            {
                continue;
            }

            ItemConsumption(holder, HeldItems.RedCard);
            _forcedOut.Add(user);
        }
    }

    /// <summary>The end of the action: drags first (no decision asked), then the mid-turn switch request.</summary>
    private void ResolveSwitchFlags()
    {
        foreach (var side in _sides.Values)
        {
            for (var slot = 0; slot < side.Active.Count; slot++)
            {
                if (side.Active[slot] is { IsFainted: false } pokemon && _forcedOut.Contains(pokemon))
                {
                    DragOut(side, slot, pokemon);
                }
            }
        }

        if (_switchFlag is { Pokemon: { IsFainted: false } switching })
        {
            var (side, slot) = Locate(switching);
            if (slot >= 0 && side.Bench.Count > 0)
            {
                Request(new DecisionRequest(side.Id, DecisionKind.MidTurnSwitch, [slot]));
            }
        }

        ClearSwitchFlags();
    }

    /// <summary>Showdown's dragIn: a random healthy benched ally replaces the Pokémon, abilities included.</summary>
    private void DragOut(Side side, int slot, BattlePokemon pokemon)
    {
        var bench = side.Bench;
        if (bench.Count == 0)
        {
            return;
        }

        var incoming = bench[_random.Next(0, bench.Count)];
        _events.Add(new ForcedOut(side.Id, slot, pokemon.Set.Species));
        SwitchIn(side, slot, incoming);
    }
}
