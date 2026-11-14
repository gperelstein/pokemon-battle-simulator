using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Immunity, absorb and prevention abilities, following Showdown's data/abilities.ts with the
/// Gen 6 mods (Soundproof from gen7, Magic Guard from gen6). While a Mold Breaker user's move
/// runs, every breakable ability is ignored, including on the user's side (Gen 6 suppressingAbility).
/// </summary>
public sealed partial class Battle
{
    private static readonly HashSet<string> SelfDestructingMoves = ["explosion", "selfdestruct"];

    /// <summary>The Pokémon whose Mold Breaker move is being executed, if any.</summary>
    private BattlePokemon? _moldBreakerUser;

    /// <summary>Effective ability: null while a Mold Breaker move ignores it.</summary>
    private string? AbilityOf(BattlePokemon pokemon) =>
        _moldBreakerUser is not null && pokemon.AbilityBreakable ? null : pokemon.Ability;

    private void AbilityImmunity(BattlePokemon pokemon, string ability)
    {
        var (side, slot) = Locate(pokemon);
        _events.Add(new AbilityImmune(side.Id, slot, pokemon.Set.Species, ability));
    }

    private void AbilityActivation(BattlePokemon pokemon, string ability)
    {
        var (side, slot) = Locate(pokemon);
        _events.Add(new AbilityActivated(side.Id, slot, pokemon.Set.Species, ability));
    }

    /// <summary>
    /// The target's TryHit abilities, which run after type immunity and before accuracy in Gen 6:
    /// Volt Absorb, Water Absorb, Dry Skin, Flash Fire, Lightning Rod, Soundproof, Sturdy (against
    /// OHKO moves) and Overcoat (against powder). Returns true when the move stops at this target.
    /// </summary>
    private bool AbsorbedByAbility(BattlePokemon user, BattlePokemon target, MoveDefinition move)
    {
        var ability = AbilityOf(target);
        if (ability is null)
        {
            return false;
        }

        // Soundproof (Gen 6/7 version) has no "not the user" check; every other one does.
        if (ability == "soundproof" && move.Flags.HasFlag(MoveFlags.Sound))
        {
            AbilityImmunity(target, ability);
            return true;
        }

        if (target == user)
        {
            return false;
        }

        switch (ability)
        {
            case "voltabsorb" when move.Type == PokemonType.Electric:
            case "waterabsorb" or "dryskin" when move.Type == PokemonType.Water:
                if (!HealByAbility(target, ability, target.Set.Stats.Hp / 4))
                {
                    AbilityImmunity(target, ability);
                }

                return true;

            case "flashfire" when move.Type == PokemonType.Fire:
                if (target.FlashFireActive)
                {
                    AbilityImmunity(target, ability);
                }
                else
                {
                    target.FlashFireActive = true;
                    AbilityActivation(target, ability);
                }

                return true;

            case "lightningrod" when move.Type == PokemonType.Electric:
                if (target.GetStage(BoostStat.SpecialAttack) == StatStages.Max)
                {
                    AbilityImmunity(target, ability);
                }
                else
                {
                    AbilityActivation(target, ability);
                    ChangeStages(target, [new StatChange(BoostStat.SpecialAttack, 1)], announceLimit: false, source: target);
                }

                return true;

            case "sturdy" when move.Ohko:
                AbilityImmunity(target, ability);
                return true;

            // Overcoat speaks for powder only when the type itself would not stop it (Grass).
            case "overcoat" when move.Powder && !_typeChart.IsImmune(target.Set.Types, TypeCondition.Powder):
                AbilityImmunity(target, ability);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Heals with an ability (Volt Absorb, Dry Skin...); false when already at full HP.</summary>
    private bool HealByAbility(BattlePokemon pokemon, string ability, int amount)
    {
        if (pokemon.CurrentHp == pokemon.Set.Stats.Hp)
        {
            return false;
        }

        AbilityActivation(pokemon, ability);
        var (side, slot) = Locate(pokemon);
        var healed = pokemon.Heal(Math.Max(1, amount));
        _events.Add(new Healed(side.Id, slot, pokemon.Set.Species, healed, pokemon.CurrentHp));
        return true;
    }

    /// <summary>Ability that stops <paramref name="status"/> on the target (Showdown's onSetStatus), if any.</summary>
    private string? StatusBlockingAbility(BattlePokemon target, MajorStatus status) =>
        (AbilityOf(target), status) switch
        {
            ("immunity", MajorStatus.Poison or MajorStatus.Toxic) => "immunity",
            ("insomnia" or "vitalspirit", MajorStatus.Sleep) => AbilityOf(target),
            ("limber", MajorStatus.Paralysis) => "limber",
            ("waterveil", MajorStatus.Burn) => "waterveil",
            ("leafguard", _) when EffectiveWeather == Battles.Weather.Sun => "leafguard",
            _ => null,
        };

    /// <summary>
    /// Removes the drops another Pokémon tries to inflict on a Clear Body (all stats), Hyper Cutter
    /// (Attack), Big Pecks (Defense) or Keen Eye (accuracy) holder. Self-inflicted drops are kept.
    /// The prevention is announced for primary effects only, as Showdown hides it for secondaries.
    /// </summary>
    private IReadOnlyList<StatChange> WithoutBlockedDrops(
        BattlePokemon target, BattlePokemon? source, IReadOnlyList<StatChange> changes, bool announce)
    {
        var ability = AbilityOf(target);
        if (source is null || source == target || ability is null)
        {
            return changes;
        }

        bool Blocks(StatChange change) => change.Stages < 0 && ability switch
        {
            "clearbody" => true,
            "hypercutter" => change.Stat == BoostStat.Attack,
            "bigpecks" => change.Stat == BoostStat.Defense,
            "keeneye" => change.Stat == BoostStat.Accuracy,
            _ => false,
        };

        if (!changes.Any(Blocks))
        {
            return changes;
        }

        if (announce)
        {
            var (side, slot) = Locate(target);
            _events.Add(new StatDropPrevented(side.Id, slot, target.Set.Species, ability));
        }

        return changes.Where(change => !Blocks(change)).ToArray();
    }

    /// <summary>
    /// Lightning Rod draws single-target Electric moves in doubles (Showdown's onAnyRedirectTarget):
    /// the fastest other active holder, ally or foe, becomes the target. Spread moves are not drawn.
    /// </summary>
    private IReadOnlyList<(Side Side, int Slot)> RedirectToLightningRod(
        MoveDefinition move, BattlePokemon user, IReadOnlyList<(Side Side, int Slot)> targets)
    {
        if (Format.ActiveSlots() < 2 || move.Type != PokemonType.Electric || targets.Count != 1 ||
            move.Target is not (MoveTarget.Normal or MoveTarget.AdjacentFoe or MoveTarget.Any or MoveTarget.RandomNormal))
        {
            return targets;
        }

        var holder = _sides.Values
            .SelectMany(side => side.LiveSlots.Select(slot => side.Active[slot]!))
            .Where(pokemon => pokemon != user && AbilityOf(pokemon) == "lightningrod")
            .OrderByDescending(pokemon => pokemon.Speed)
            .FirstOrDefault();
        if (holder is null)
        {
            return targets;
        }

        var (side, slot) = Locate(holder);
        if ((side, slot) == targets[0])
        {
            return targets;
        }

        AbilityActivation(holder, "lightningrod");
        return [(side, slot)];
    }

    /// <summary>Damp stops Explosion and Self-Destruct before the user faints. Returns true if it did.</summary>
    private bool StoppedByDamp(MoveAction action, MoveDefinition move)
    {
        if (!SelfDestructingMoves.Contains(move.Id))
        {
            return false;
        }

        var holder = _sides.Values
            .SelectMany(side => side.LiveSlots.Select(slot => side.Active[slot]!))
            .FirstOrDefault(pokemon => AbilityOf(pokemon) == "damp");
        if (holder is null)
        {
            return false;
        }

        AbilityActivation(holder, "damp");
        _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "Damp prevents self-destruction."));
        return true;
    }

    /// <summary>Sturdy: a hit that would knock out a Pokémon at full HP leaves it at 1 HP.</summary>
    private int EnduredBySturdy(BattlePokemon target, int damage)
    {
        if (AbilityOf(target) != "sturdy" || target.CurrentHp != target.Set.Stats.Hp || damage < target.CurrentHp)
        {
            return damage;
        }

        AbilityActivation(target, "sturdy");
        return target.CurrentHp - 1;
    }

    /// <summary>Magic Guard: only direct attacks can damage its holder (not weather or status).</summary>
    private static bool IgnoresIndirectDamage(BattlePokemon pokemon) => pokemon.Ability == "magicguard";
}
