using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Moves and abilities that use, take or move held items, following Showdown's data/moves.ts (Gen 6
/// inherits Knock Off, Thief, Covet, Trick, Fling, Natural Gift, Acrobatics, Bug Bite, Pluck,
/// Incinerate and Recycle from master; gen7 only rewrites Switcheroo's failure check the same way),
/// data/abilities.ts (Frisk, Pickup, Sticky Hold, Unburden: the gen6-gen8 mods only change ratings)
/// and sim/pokemon.ts takeItem / setItem:
/// <list type="bullet">
/// <item>takeItem runs the TakeItem event: the item's onTakeItem keeps Mega Stones (and form items)
/// with their species (<see cref="ItemDefinition.BoundSpecies"/>), and Sticky Hold (breakable, so
/// Mold Breaker ignores it) keeps a living holder's item from another Pokémon's move or Knock Off.
/// Moving an item also checks the receiver's species (the singleEvent the moves run).</item>
/// <item>setItem runs the new item's onStart: a Choice item drops its holder's choice lock.</item>
/// <item>Order inside a hit (sim/battle-actions.ts spreadMoveHit): the move's onHit (Bug Bite, Pluck,
/// Incinerate, Trick, a flung berry or White Herb) before the target's onHit items, the flung
/// item's status with the secondaries, then DamagingHit, then onAfterHit while the user stands
/// (Knock Off, Thief, Covet), then the Update event where the fling volatile drops the item.</item>
/// </list>
/// Not modelled for lack of the mechanism: the flinch of a flung King's Rock or Razor Fang
/// (flinch) and a flung Mental Herb's cure (Attract, Taunt, Encore, Torment, Disable, Heal Block).
/// </summary>
public sealed partial class Battle
{
    /// <summary>The item the current Fling throws (its fling volatile), or null.</summary>
    private ItemDefinition? _flungItem;

    /// <summary>The item cannot leave, or reach, a Pokémon of its bound species (onTakeItem).</summary>
    private static bool IsBound(ItemDefinition item, BattlePokemon pokemon) =>
        item.BoundSpecies.Contains(pokemon.Set.BaseSpecies ?? pokemon.Set.Species);

    /// <summary>
    /// Showdown's takeItem TakeItem event: whether <paramref name="holder"/>'s item can be removed by
    /// <paramref name="taker"/>'s move (the holder itself for Knock Off, which passes no source).
    /// Sticky Hold announces itself when it stops another Pokémon's move.
    /// </summary>
    private bool CanTakeItem(BattlePokemon holder, BattlePokemon taker, string moveId)
    {
        if (holder.HeldItem is not { } item || IsBound(item, holder))
        {
            return false;
        }

        if (AbilityOf(holder) == "stickyhold" && !holder.IsFainted && item.Id != HeldItems.StickyBarb &&
            (taker != holder || moveId == "knockoff"))
        {
            AbilityActivation(holder, "stickyhold");
            return false;
        }

        return true;
    }

    /// <summary>Showdown's setItem: the Pokémon holds the item and the item starts.</summary>
    private static void ReceiveItem(BattlePokemon pokemon, ItemDefinition item)
    {
        pokemon.SetItem(item);
        ItemStarted(pokemon);
    }

    /// <summary>The onStart of an item the Pokémon just got: a Choice item drops the choice lock.</summary>
    private static void ItemStarted(BattlePokemon pokemon)
    {
        if (IsChoiceItem(pokemon.Item))
        {
            pokemon.ChoiceLockedMoveId = null;
        }
    }

    /// <summary>
    /// The held item's effect on the move before it is announced: Fling's power (set in its
    /// onPrepareHit, used by the damage only) and Natural Gift's type (onModifyType) and power.
    /// </summary>
    private static MoveDefinition ItemPoweredMove(MoveDefinition move, BattlePokemon user) => (move.Id, user.HeldItem) switch
    {
        ("fling", { FlingPower: > 0 } item) => move with { BasePower = item.FlingPower },
        ("naturalgift", { NaturalGiftType: { } type } item) => move with { Type = type, BasePower = item.NaturalGiftPower },
        _ => move,
    };

    /// <summary>
    /// The move's onPrepareHit, before any hit check: Fling fails without an item it can throw (no
    /// fling data, or bound to the user) and readies its fling volatile; Natural Gift fails without a
    /// berry and uses it up at once. Returns false when the move fails.
    /// </summary>
    private bool PrepareItemMove(MoveAction action, MoveDefinition move)
    {
        var user = action.Actor;
        switch (move.Id)
        {
            case "fling":
                if (user.HeldItem is not { FlingPower: > 0 } item || IsBound(item, user))
                {
                    _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "It has no item it can fling."));
                    return false;
                }

                _flungItem = item;
                return true;

            case "naturalgift":
                if (user.HeldItem is not { NaturalGiftType: not null } berry)
                {
                    _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "It has no berry to use."));
                    return false;
                }

                ItemConsumption(user, berry.Id);
                return true;

            default:
                return true;
        }
    }

    /// <summary>
    /// The move's onHit on one target it hit (fainted ones included): Bug Bite and Pluck eat the
    /// target's berry, Incinerate burns its berry or gem, Trick and Switcheroo swap items, Recycle
    /// restores the last item, and a flung berry or White Herb acts on the target.
    /// </summary>
    private void ItemMoveOnHit(MoveAction action, MoveDefinition move, BattlePokemon target)
    {
        var user = action.Actor;
        switch (move.Id)
        {
            case "bugbite" or "pluck" when !user.IsFainted && Berries.IsBerry(target.Item) && CanTakeItem(target, user, move.Id):
                var berry = RemoveItem(target, move.Id);
                ApplyBerryEffect(user, berry.Id);
                break;

            // Gen 6 burns gems too; Normal Gem is the only Gen 6 gem.
            case "incinerate" when (Berries.IsBerry(target.Item) || target.Item == HeldItems.NormalGem) &&
                                   CanTakeItem(target, user, move.Id):
                RemoveItem(target, move.Id);
                break;

            case "trick" or "switcheroo":
                SwapItems(action, move, target);
                break;

            case "recycle":
                Recycle(action, move);
                break;

            case "fling" when _flungItem is { } flung && !target.IsFainted:
                if (Berries.IsBerry(flung.Id))
                {
                    ApplyBerryEffect(target, flung.Id);
                }
                else if (flung.Id == HeldItems.WhiteHerb)
                {
                    // White Herb's fling effect: silent ("-clearnegativeboost [silent]").
                    target.ClearNegativeStages();
                }

                break;
        }
    }

    /// <summary>Takes the item away for good (Knock Off, Incinerate, Bug Bite, Pluck): "-enditem [from] move".</summary>
    private ItemDefinition RemoveItem(BattlePokemon holder, string moveId)
    {
        var item = holder.LoseItem()!;
        var (side, slot) = Locate(holder);
        _events.Add(new ItemRemoved(side.Id, slot, holder.Set.Species, item.Id, moveId));
        return item;
    }

    /// <summary>
    /// Trick / Switcheroo onHit: both items are taken (the target's by the user, the user's by
    /// itself, so the user's Sticky Hold does not stop it), each must be allowed to reach the other
    /// side, and at least one of them must hold something; otherwise the move fails.
    /// </summary>
    private void SwapItems(MoveAction action, MoveDefinition move, BattlePokemon target)
    {
        var user = action.Actor;
        var yours = target.HeldItem;
        var mine = user.HeldItem;
        var blocked = (yours is not null && !CanTakeItem(target, user, move.Id)) ||
                      (mine is not null && !CanTakeItem(user, user, move.Id)) ||
                      (mine is not null && IsBound(mine, target)) ||
                      (yours is not null && IsBound(yours, user));
        if (blocked || (yours is null && mine is null))
        {
            user.MoveSucceededThisTurn = false;
            _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "The items cannot be swapped."));
            return;
        }

        target.LoseItem();
        user.LoseItem();
        if (mine is not null)
        {
            ReceiveItem(target, mine);
        }

        if (yours is not null)
        {
            ReceiveItem(user, yours);
        }

        var (targetSide, targetSlot) = Locate(target);
        _events.Add(new ItemsSwapped(action.Side.Id, action.Slot, user.Set.Species, user.Item, targetSide.Id, targetSlot,
            target.Set.Species, target.Item, move.Id));
    }

    /// <summary>Recycle onHit: fails while holding an item or without a last item, otherwise restores it.</summary>
    private void Recycle(MoveAction action, MoveDefinition move)
    {
        var user = action.Actor;
        if (user.Item is not null || user.LastItem is null)
        {
            user.MoveSucceededThisTurn = false;
            _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "There is no item to recycle."));
            return;
        }

        user.RestoreLastItem();
        ItemStarted(user);

        _events.Add(new ItemReceived(action.Side.Id, action.Slot, user.Set.Species, user.Item!));
    }

    /// <summary>
    /// onAfterHit, for every target the move damaged, while the user still stands: Knock Off removes
    /// the target's item; Thief and Covet take it when the user holds nothing and used no gem.
    /// </summary>
    private void ItemMovesAfterHit(BattlePokemon user, MoveDefinition move, IEnumerable<BattlePokemon> damaged)
    {
        foreach (var target in damaged)
        {
            if (user.IsFainted)
            {
                return;
            }

            switch (move.Id)
            {
                case "knockoff" when CanTakeItem(target, target, move.Id):
                    RemoveItem(target, move.Id);
                    break;

                case "thief" or "covet" when user.Item is null && _gemUser != user && CanTakeItem(target, user, move.Id) &&
                                             !IsBound(target.HeldItem!, user):
                    var item = target.LoseItem()!;
                    ReceiveItem(user, item);
                    var (side, slot) = Locate(user);
                    var (targetSide, targetSlot) = Locate(target);
                    _events.Add(new ItemStolen(side.Id, slot, user.Set.Species, item.Id, targetSide.Id, targetSlot, move.Id));
                    break;
            }
        }
    }

    /// <summary>
    /// The fling volatile's onUpdate: the thrown item is used up (it becomes the last item, as with
    /// useItem), whether or not the move hit. A fainted user is not updated, so it keeps the item.
    /// </summary>
    private void UseUpFlungItem(BattlePokemon user)
    {
        if (_flungItem is { } item && !user.IsFainted && user.HeldItem == item)
        {
            ItemConsumption(user, item.Id);
        }

        _flungItem = null;
    }

    /// <summary>Frisk (onStart): reveals the item of every foe that holds one.</summary>
    private void Frisk(BattlePokemon pokemon)
    {
        var (side, slot) = Locate(pokemon);
        var foes = _sides[pokemon.Side.Opponent()];
        foreach (var foeSlot in foes.LiveSlots)
        {
            if (foes.Active[foeSlot]!.Item is { } item)
            {
                _events.Add(new ItemFrisked(side.Id, slot, pokemon.Set.Species, item, foes.Id, foeSlot));
            }
        }
    }

    /// <summary>
    /// Pickup (residual order 28, sub-order 2): a holder without an item takes the last item of a
    /// random other active Pokémon that used it this turn (in singles and doubles every other active
    /// Pokémon is adjacent); that Pokémon loses its last item. A single candidate needs no roll.
    /// </summary>
    private void Pickup(BattlePokemon pokemon)
    {
        if (pokemon.Ability != "pickup" || pokemon.Item is not null)
        {
            return;
        }

        var candidates = ActivePokemon()
            .Where(other => other != pokemon && !other.IsFainted && other.LastItem is not null && other.UsedItemThisTurn)
            .ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        var source = candidates.Length == 1 ? candidates[0] : candidates[_random.Next(0, candidates.Length)];
        var item = source.TakeLastItem()!;
        AbilityActivation(pokemon, "pickup");
        ReceiveItem(pokemon, item);
        var (side, slot) = Locate(pokemon);
        _events.Add(new ItemReceived(side.Id, slot, pokemon.Set.Species, item.Id));
    }
}
