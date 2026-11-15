using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Held items that trigger on their own and single-use items, following Showdown's data/items.ts
/// with the Gen 6 mods (gen6 Rocky Helmet passes no effect to its damage, which changes nothing
/// here; gen8 only makes Berry Juice standard): end-of-turn items (sim/battle.ts fieldEvent sorts
/// residual handlers by order, then speed, then sub-order), Focus Sash / Focus Band (onDamage
/// priority -40, after Sturdy's -30), DamagingHit reactions, Sticky Barb's onHit, Shell Bell, Air
/// Balloon (sim/pokemon.ts isGrounded), White Herb, Power Herb, Berry Juice (Update event) and
/// Safety Goggles. Damage and heals use Showdown's rounding: max(1, floor(maxHp x fraction)).
/// </summary>
public sealed partial class Battle
{
    /// <summary>Damage the current move dealt to every target (Showdown's move.totalDamage, Shell Bell).</summary>
    private int _moveDamageDealt;

    /// <summary>Targets the current move hit super effectively with calculated damage (Weakness Policy).</summary>
    private readonly HashSet<BattlePokemon> _superEffectiveTargets = [];

    private void ItemActivation(BattlePokemon pokemon, string item)
    {
        var (side, slot) = Locate(pokemon);
        _events.Add(new ItemActivated(side.Id, slot, pokemon.Set.Species, item));
    }

    /// <summary>The holder's item is used up or lost (Showdown's useItem / "-enditem").</summary>
    private void ItemConsumption(BattlePokemon pokemon, string item)
    {
        pokemon.ConsumeItem();
        var (side, slot) = Locate(pokemon);
        _events.Add(new ItemConsumed(side.Id, slot, pokemon.Set.Species, item));
    }

    /// <summary>Indirect damage from an item; Magic Guard blocks it (its onDamage rejects non-move effects).</summary>
    private void HurtByItem(BattlePokemon pokemon, string item, int amount)
    {
        if (pokemon.IsFainted || IgnoresIndirectDamage(pokemon))
        {
            return;
        }

        var (side, slot) = Locate(pokemon);
        var dealt = pokemon.TakeDamage(Math.Max(1, amount));
        pokemon.HurtThisTurn = true;
        _events.Add(new ItemDamage(side.Id, slot, pokemon.Set.Species, item, dealt, pokemon.CurrentHp));
        if (pokemon.IsFainted)
        {
            _events.Add(new Fainted(side.Id, slot, pokemon.Set.Species));
        }
    }

    /// <summary>Healing from an item ("[from] item: ..."); nothing happens at full HP, as in Showdown's heal.</summary>
    private void HealByItem(BattlePokemon pokemon, string item, int amount)
    {
        if (pokemon.IsFainted || pokemon.CurrentHp == pokemon.Set.Stats.Hp)
        {
            return;
        }

        ItemActivation(pokemon, item);
        var (side, slot) = Locate(pokemon);
        var healed = pokemon.Heal(Math.Max(1, amount));
        _events.Add(new Healed(side.Id, slot, pokemon.Set.Species, healed, pokemon.CurrentHp));
    }

    /// <summary>
    /// Residual order 5, sub-order 4 (right after the holder's own order-5 ability): Leftovers heal
    /// 1/16; Black Sludge heals Poison types 1/16 and hurts the others 1/8.
    /// </summary>
    private void ApplyHealingItemResidual(BattlePokemon pokemon)
    {
        var maxHp = pokemon.Set.Stats.Hp;
        switch (pokemon.Item)
        {
            case HeldItems.Leftovers:
            case HeldItems.BlackSludge when pokemon.Set.Types.Contains(PokemonType.Poison):
                HealByItem(pokemon, pokemon.Item, maxHp / 16);
                break;

            case HeldItems.BlackSludge:
                HurtByItem(pokemon, HeldItems.BlackSludge, maxHp / 8);
                break;
        }
    }

    /// <summary>
    /// Residual order 28 (sub-order 3), after poison and burn: Sticky Barb hurts its holder 1/8,
    /// Toxic Orb and Flame Orb afflict it (trySetStatus: silent when it already has a status or is
    /// immune). Then order 29: White Herb. Fainted holders are skipped, as in fieldEvent.
    /// </summary>
    /// <param name="bySpeed">Active Pokémon sorted when the residual phase started (handler speeds).</param>
    private void ApplyLateItemResiduals(IReadOnlyList<BattlePokemon> bySpeed)
    {
        foreach (var pokemon in bySpeed.Where(p => !p.IsFainted))
        {
            switch (pokemon.Item)
            {
                case HeldItems.StickyBarb:
                    HurtByItem(pokemon, HeldItems.StickyBarb, pokemon.Set.Stats.Hp / 8);
                    break;

                case HeldItems.ToxicOrb:
                    SetStatus(pokemon, MajorStatus.Toxic, pokemon, primary: null, viaAbility: null, viaItem: HeldItems.ToxicOrb);
                    break;

                case HeldItems.FlameOrb:
                    SetStatus(pokemon, MajorStatus.Burn, pokemon, primary: null, viaAbility: null, viaItem: HeldItems.FlameOrb);
                    break;
            }
        }

        foreach (var pokemon in bySpeed)
        {
            RestoreLoweredStages(pokemon);
        }
    }

    /// <summary>
    /// White Herb: once any stat stage is below 0, it is used up and every lowered stage returns to
    /// 0. Showdown checks after every move (onAnyAfterMove), after switch-ins (onAnySwitchIn, priority
    /// -2, so after Intimidate) and at residual order 29.
    /// </summary>
    private void RestoreLoweredStages(BattlePokemon pokemon)
    {
        if (pokemon.IsFainted || pokemon.Item != HeldItems.WhiteHerb || !pokemon.HasNegativeStages)
        {
            return;
        }

        ItemConsumption(pokemon, HeldItems.WhiteHerb);
        pokemon.ClearNegativeStages();
        var (side, slot) = Locate(pokemon);
        _events.Add(new NegativeStagesCleared(side.Id, slot, pokemon.Set.Species));
    }

    private void RestoreLoweredStagesOfActivePokemon()
    {
        foreach (var pokemon in ActivePokemon().OrderByDescending(p => p.Speed).ToArray())
        {
            RestoreLoweredStages(pokemon);
        }
    }

    /// <summary>The item's start on switch-in (sub-order 8, after the ability): Air Balloon announces itself.</summary>
    private void StartItem(BattlePokemon pokemon)
    {
        if (pokemon.Item == HeldItems.AirBalloon)
        {
            ItemActivation(pokemon, HeldItems.AirBalloon);
        }
    }

    /// <summary>
    /// Items of Showdown's Update event, run after every action, after each hit and after the end of
    /// the turn: Berry Juice heals 20 HP, once, when its holder is at half HP or less.
    /// </summary>
    private void UpdateItems()
    {
        foreach (var pokemon in ActivePokemon().OrderByDescending(p => p.Speed).ToArray())
        {
            if (pokemon.Item == HeldItems.BerryJuice && !pokemon.IsFainted && pokemon.CurrentHp * 2 <= pokemon.Set.Stats.Hp)
            {
                ItemConsumption(pokemon, HeldItems.BerryJuice);
                var (side, slot) = Locate(pokemon);
                var healed = pokemon.Heal(20);
                _events.Add(new Healed(side.Id, slot, pokemon.Set.Species, healed, pokemon.CurrentHp));
            }
        }
    }

    /// <summary>
    /// Air Balloon keeps its holder off the ground (isGrounded), so moves that check type immunity
    /// cannot hit it with Ground; Showdown reports a plain immunity.
    /// </summary>
    private static bool FloatsOnAirBalloon(BattlePokemon target, MoveDefinition move) =>
        !move.IgnoresTypeImmunity && move.Type == PokemonType.Ground && target.Item == HeldItems.AirBalloon;

    /// <summary>
    /// Safety Goggles (onTryHit, after the target's TryHit abilities such as Overcoat): powder moves
    /// from another Pokémon fail, unless the holder's type is already immune (then the natural powder
    /// immunity speaks).
    /// </summary>
    private bool BlockedBySafetyGoggles(BattlePokemon user, BattlePokemon target, MoveDefinition move)
    {
        if (!move.Powder || target == user || target.Item != HeldItems.SafetyGoggles ||
            _typeChart.IsImmune(target.Set.Types, TypeCondition.Powder))
        {
            return false;
        }

        ItemActivation(target, HeldItems.SafetyGoggles);
        return true;
    }

    /// <summary>
    /// Focus Sash (used up) leaves a holder at full HP with 1 HP against a move that would knock it
    /// out; Focus Band does the same one time in ten and stays. Both run after Sturdy. Showdown rolls
    /// Focus Band's chance on every damage event before checking for a knock-out; the engine rolls
    /// only when the hit would knock the holder out, which keeps the same 10% outcome.
    /// </summary>
    private int EnduredByItem(BattlePokemon target, int damage)
    {
        if (damage < target.CurrentHp)
        {
            return damage;
        }

        switch (target.Item)
        {
            case HeldItems.FocusSash when target.CurrentHp == target.Set.Stats.Hp:
                ItemConsumption(target, HeldItems.FocusSash);
                return target.CurrentHp - 1;

            case HeldItems.FocusBand when _random.Next(0, 10) == 0:
                ItemActivation(target, HeldItems.FocusBand);
                return target.CurrentHp - 1;

            default:
                return damage;
        }
    }

    /// <summary>
    /// Sticky Barb (the holder's onHit): a contact move from a Pokémon without an item takes the
    /// barb, even from a holder it has just knocked out. Showdown prints no message for it.
    /// </summary>
    private static void PassStickyBarb(BattlePokemon attacker, BattlePokemon holder, MoveDefinition move)
    {
        if (holder == attacker || holder.Item != HeldItems.StickyBarb || attacker.Item is not null ||
            !move.Flags.HasFlag(MoveFlags.Contact))
        {
            return;
        }

        holder.ConsumeItem();
        if (!attacker.IsFainted)
        {
            attacker.SetItem(HeldItems.StickyBarb);
        }
    }

    /// <summary>
    /// Rocky Helmet (DamagingHit order 2, after Aftermath and before the other reactions): a contact
    /// attacker loses 1/6 of its max HP, even when the holder fainted.
    /// </summary>
    private void RockyHelmetOnDamagingHit(BattlePokemon attacker, BattlePokemon holder, MoveDefinition move)
    {
        if (holder.Item == HeldItems.RockyHelmet && move.Flags.HasFlag(MoveFlags.Contact))
        {
            HurtByItem(attacker, HeldItems.RockyHelmet, attacker.Set.Stats.Hp / 6);
        }
    }

    /// <summary>
    /// The target's item reacting to a damaging hit (after its ability, before Poison Touch): an Air
    /// Balloon pops (even on a fainted holder); Weakness Policy (+2 Attack and Special Attack after a
    /// super-effective hit whose damage is calculated) and Absorb Bulb, Cell Battery, Luminous Moss,
    /// Snowball (+1 to their stat after a hit of their type) are used up by a holder still standing.
    /// </summary>
    private void ItemReactionToDamagingHit(BattlePokemon target, PokemonType moveType)
    {
        if (target.Item == HeldItems.AirBalloon)
        {
            ItemConsumption(target, HeldItems.AirBalloon);
            return;
        }

        if (target.IsFainted)
        {
            return;
        }

        if (target.Item == HeldItems.WeaknessPolicy && _superEffectiveTargets.Contains(target))
        {
            ItemConsumption(target, HeldItems.WeaknessPolicy);
            ChangeStages(target, [new StatChange(BoostStat.Attack, 2), new StatChange(BoostStat.SpecialAttack, 2)],
                announceLimit: true, source: target);
        }
        else if (HeldItems.TypeReaction(target.Item, moveType) is { } stat)
        {
            ItemConsumption(target, target.Item!);
            ChangeStages(target, [new StatChange(stat, 1)], announceLimit: true, source: target);
        }
    }

    /// <summary>
    /// Shell Bell (onAfterMoveSecondarySelf): the holder recovers 1/8 of the damage its move dealt,
    /// at least 1; not when Sheer Force suppressed the move's secondary effects.
    /// </summary>
    private void ApplyShellBell(BattlePokemon user, bool sheerForce)
    {
        // Showdown: `move.totalDamage && !pokemon.forceSwitchFlag`, so no heal for a user that Red Card
        // (or a forced-switch move) is about to drag out.
        if (user.Item == HeldItems.ShellBell && _moveDamageDealt > 0 && !sheerForce && !_forcedOut.Contains(user))
        {
            HealByItem(user, HeldItems.ShellBell, _moveDamageDealt / 8);
        }
    }
}
