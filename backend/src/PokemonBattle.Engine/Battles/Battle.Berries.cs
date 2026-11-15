using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Berries and the abilities around them, following Showdown's data/items.ts with the gen6 item mod
/// (Figy, Wiki, Mago, Aguav and Iapapa heal 1/8 at half HP; Jaboca and Rowap only skip a Magic Guard
/// attacker), data/abilities.ts (Unnerve, Gluttony, Harvest) and sim/pokemon.ts eatItem. Every berry
/// goes through <see cref="EatItem"/>: the berry is announced ("-enditem [eat]", an
/// <see cref="ItemConsumed"/> event), becomes the holder's last item, and its onEat effect runs
/// (<see cref="ApplyBerryEffect"/>). Bug Bite, Pluck and Fling can reuse that effect for another eater.
/// <list type="bullet">
/// <item>Update event: status berries, Lum, Oran, Sitrus, the Figy group and the pinch berries.</item>
/// <item>Lum also on AfterSetStatus (priority -1, so after Synchronize).</item>
/// <item>Damage calculation: type-resist berries and Chilan (onSourceModifyDamage).</item>
/// <item>onHit: Enigma; DamagingHit: Jaboca, Rowap; AfterMoveSecondary: Kee, Maranga.</item>
/// <item>Turn order: Custap (onFractionalPriority, when the queue is built).</item>
/// <item>End of turn: Micle (onResidual without an order, so last), Harvest (order 28, sub-order 2).</item>
/// </list>
/// Not implemented, for lack of the mechanism: Leppa (PP), Persim and the confusion of Lum and the
/// Figy group (confusion; the Figy group also needs natures). The other berries (Razz, Pomeg...) are
/// never eaten by their holder.
/// </summary>
public sealed partial class Battle
{
    /// <summary>Starf Berry's candidates, in Showdown's boost-table order (accuracy and evasion excluded).</summary>
    private static readonly BoostStat[] StarfStats =
        [BoostStat.Attack, BoostStat.Defense, BoostStat.SpecialAttack, BoostStat.SpecialDefense, BoostStat.Speed];

    /// <summary>Pinch berries eaten on Update and the stat they raise by 1 (Starf and Lansat apart).</summary>
    private static readonly Dictionary<string, BoostStat> PinchBerries = new()
    {
        [Berries.Liechi] = BoostStat.Attack,
        [Berries.Ganlon] = BoostStat.Defense,
        [Berries.Salac] = BoostStat.Speed,
        [Berries.Petaya] = BoostStat.SpecialAttack,
        [Berries.Apicot] = BoostStat.SpecialDefense,
    };

    /// <summary>
    /// Showdown's eatItem: a held berry is eaten unless its holder has fainted (Jaboca and Rowap
    /// excepted) or left the field, or a foe's Unnerve stops it (onFoeTryEatItem).
    /// </summary>
    private bool EatItem(BattlePokemon holder)
    {
        var berry = holder.Item;
        if (!Berries.IsBerry(berry) || Locate(holder).Slot < 0 ||
            (holder.IsFainted && berry is not (Berries.Jaboca or Berries.Rowap)) || IsUnnerved(holder))
        {
            return false;
        }

        ItemConsumption(holder, berry!);
        ApplyBerryEffect(holder, berry!);
        return true;
    }

    /// <summary>A foe on the field has Unnerve, so the Pokémon cannot eat berries.</summary>
    private bool IsUnnerved(BattlePokemon pokemon) => FoesOf(pokemon).Any(foe => foe.Ability == "unnerve");

    /// <summary>
    /// The berry's onEat effect on <paramref name="eater"/>. Berries whose effect is part of their
    /// trigger (type-resist berries, Chilan, Custap, Enigma, Jaboca, Rowap) do nothing here.
    /// </summary>
    private void ApplyBerryEffect(BattlePokemon eater, string berry)
    {
        var maxHp = eater.Set.Stats.Hp;
        switch (berry)
        {
            case Berries.Cheri when eater.Status == MajorStatus.Paralysis:
            case Berries.Chesto when eater.Status == MajorStatus.Sleep:
            case Berries.Pecha when eater.Status is MajorStatus.Poison or MajorStatus.Toxic:
            case Berries.Rawst when eater.Status == MajorStatus.Burn:
            case Berries.Aspear when eater.Status == MajorStatus.Freeze:
            case Berries.Lum when eater.Status is not null:
                Cure(eater);
                break;

            case Berries.Oran:
                HealByBerry(eater, 10);
                break;

            case Berries.Sitrus:
                HealByBerry(eater, maxHp / 4);
                break;

            case not null when Berries.FlavorHealers.Contains(berry):
                HealByBerry(eater, maxHp / 8);
                break;

            case not null when PinchBerries.TryGetValue(berry, out var stat):
                ChangeStages(eater, [new StatChange(stat, 1)], announceLimit: true, source: eater);
                break;

            case Berries.Kee:
                ChangeStages(eater, [new StatChange(BoostStat.Defense, 1)], announceLimit: true, source: eater);
                break;

            case Berries.Maranga:
                ChangeStages(eater, [new StatChange(BoostStat.SpecialDefense, 1)], announceLimit: true, source: eater);
                break;

            case Berries.Starf:
                var candidates = StarfStats.Where(s => eater.GetStage(s) < StatStages.Max).ToArray();
                if (candidates.Length > 0)
                {
                    var raised = candidates[_random.Next(0, candidates.Length)];
                    ChangeStages(eater, [new StatChange(raised, 2)], announceLimit: true, source: eater);
                }

                break;

            case Berries.Lansat when !eater.FocusEnergy:
                eater.FocusEnergy = true;
                var (side, slot) = Locate(eater);
                _events.Add(new ConditionStarted(side.Id, slot, eater.Set.Species, "focusenergy"));
                break;

            case Berries.Micle:
                eater.MicleTurns = 2;
                break;
        }
    }

    /// <summary>A berry's heal: max(1, floor(amount)), nothing at full HP (Showdown's heal).</summary>
    private void HealByBerry(BattlePokemon pokemon, int amount)
    {
        if (pokemon.IsFainted || pokemon.CurrentHp == pokemon.Set.Stats.Hp)
        {
            return;
        }

        var (side, slot) = Locate(pokemon);
        var healed = pokemon.Heal(Math.Max(1, amount));
        _events.Add(new Healed(side.Id, slot, pokemon.Set.Species, healed, pokemon.CurrentHp));
    }

    /// <summary>Pinch berries' threshold: a quarter of max HP, or half with Gluttony (hp &lt;= maxhp / 4).</summary>
    private static bool InPinch(BattlePokemon pokemon) =>
        pokemon.CurrentHp * 4 <= pokemon.Set.Stats.Hp ||
        (pokemon.CurrentHp * 2 <= pokemon.Set.Stats.Hp && pokemon.Ability == "gluttony");

    /// <summary>
    /// The berries' onUpdate conditions: a matching status (Lum: any), half HP for Oran, Sitrus and
    /// the Figy group (gen6 mod: no Gluttony branch), the pinch threshold for the stat berries.
    /// </summary>
    private static bool EatsOnUpdate(BattlePokemon pokemon) => pokemon.Item switch
    {
        Berries.Cheri => pokemon.Status == MajorStatus.Paralysis,
        Berries.Chesto => pokemon.Status == MajorStatus.Sleep,
        Berries.Pecha => pokemon.Status is MajorStatus.Poison or MajorStatus.Toxic,
        Berries.Rawst => pokemon.Status == MajorStatus.Burn,
        Berries.Aspear => pokemon.Status == MajorStatus.Freeze,
        Berries.Lum => pokemon.Status is not null,
        Berries.Oran or Berries.Sitrus => pokemon.CurrentHp * 2 <= pokemon.Set.Stats.Hp,
        { } berry when Berries.FlavorHealers.Contains(berry) => pokemon.CurrentHp * 2 <= pokemon.Set.Stats.Hp,
        { } berry when PinchBerries.ContainsKey(berry) => InPinch(pokemon),
        Berries.Starf or Berries.Lansat => InPinch(pokemon),
        _ => false,
    };

    /// <summary>
    /// Enigma Berry (onHit): a holder still standing after a super-effective hit whose damage was
    /// calculated eats it and heals 1/4 of its max HP.
    /// </summary>
    private void EnigmaOnHit(BattlePokemon target)
    {
        if (target.Item == Berries.Enigma && !target.IsFainted && _superEffectiveTargets.Contains(target) && EatItem(target))
        {
            HealByBerry(target, target.Set.Stats.Hp / 4);
        }
    }

    /// <summary>
    /// Jaboca (physical) and Rowap (special) Berries, DamagingHit: the attacker loses 1/8 of its max HP,
    /// unless it has Magic Guard (then the berry stays). Gen 6 eats them even when the attacker has
    /// fainted. Returns whether one was eaten.
    /// </summary>
    private bool RetaliatingBerry(BattlePokemon attacker, BattlePokemon holder, MoveDefinition move)
    {
        var category = holder.Item switch
        {
            Berries.Jaboca => MoveCategory.Physical,
            Berries.Rowap => MoveCategory.Special,
            _ => (MoveCategory?)null,
        };
        if (category != move.Category || attacker.Ability == "magicguard")
        {
            return false;
        }

        var berry = holder.Item!;
        if (!EatItem(holder))
        {
            return false;
        }

        HurtByItem(attacker, berry, attacker.Set.Stats.Hp / 8);
        return true;
    }

    /// <summary>
    /// Kee (physical) and Maranga (special) Berries, AfterMoveSecondary: eaten after a hit of their
    /// category, except Present's heal.
    /// </summary>
    private void DefenseBerryAfterHit(BattlePokemon holder, MoveDefinition move, bool heals)
    {
        var category = holder.Item switch
        {
            Berries.Kee => MoveCategory.Physical,
            Berries.Maranga => MoveCategory.Special,
            _ => (MoveCategory?)null,
        };
        if (category == move.Category && !heals)
        {
            EatItem(holder);
        }
    }

    /// <summary>
    /// Custap Berry (onFractionalPriority, run when the turn's queue is built for every move action):
    /// at the pinch threshold it is eaten and its holder moves first in its priority bracket.
    /// </summary>
    private bool CustapActivates(BattlePokemon actor) => actor.Item == Berries.Custap && InPinch(actor) && EatItem(actor);

    /// <summary>
    /// End of the turn, after every ordered residual (Micle's onResidual has no order): an active Micle
    /// boost loses a turn (duration 2), then holders at the pinch threshold eat their Micle Berry.
    /// </summary>
    private void ApplyMicleResidual(IReadOnlyList<BattlePokemon> bySpeed)
    {
        foreach (var pokemon in bySpeed.Where(p => !p.IsFainted))
        {
            if (pokemon.MicleTurns > 0)
            {
                pokemon.MicleTurns--;
            }

            if (pokemon.Item == Berries.Micle && InPinch(pokemon))
            {
                EatItem(pokemon);
            }
        }
    }

    /// <summary>
    /// The eaten Micle Berry's onSourceAccuracy: the user's next move that reaches the Accuracy event
    /// (anything but an OHKO move or a move aimed at itself) uses it up, announced, and its numeric
    /// accuracy is chained by 4915/4096 after the stages. Returns whether it applied.
    /// </summary>
    private bool UseMicleBoost(BattlePokemon user, MoveDefinition move)
    {
        if (user.MicleTurns == 0 || move.Ohko || move.Target == MoveTarget.Self)
        {
            return false;
        }

        user.MicleTurns = 0;
        ItemActivation(user, Berries.Micle);
        return true;
    }

    /// <summary>
    /// Harvest (residual order 28, sub-order 2): a holder without an item whose last item was a berry
    /// gets it back, always in sun, otherwise one time in two. Showdown rolls the chance before
    /// checking the berry; the engine rolls only when a berry can come back, which keeps the same odds.
    /// </summary>
    private void Harvest(BattlePokemon pokemon)
    {
        if (pokemon.Ability != "harvest" || pokemon.Item is not null || !Berries.IsBerry(pokemon.LastItem) ||
            (EffectiveWeather != Battles.Weather.Sun && _random.Next(0, 2) != 0))
        {
            return;
        }

        pokemon.RestoreLastItem();
        AbilityActivation(pokemon, "harvest");
        var (side, slot) = Locate(pokemon);
        _events.Add(new ItemReceived(side.Id, slot, pokemon.Set.Species, pokemon.Item!));
    }
}
