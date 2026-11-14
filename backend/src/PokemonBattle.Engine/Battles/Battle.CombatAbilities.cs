using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Abilities that react once a move has damaged a Pokémon (Showdown's DamagingHit event, which runs
/// after the move's effects and secondaries): contact abilities, Aftermath, Justified, Rattled and
/// Weak Armor on the target, then the attacker's Poison Touch. Gen 6 versions: Aftermath and Weak
/// Armor from data/mods/gen6, Rattled without its Intimidate trigger (data/mods/gen7).
/// </summary>
public sealed partial class Battle
{
    /// <summary>Targets the current move hit critically (Anger Point).</summary>
    private readonly HashSet<BattlePokemon> _criticalTargets = [];

    /// <summary>Targets the current move damaged (DamagingHit abilities).</summary>
    private readonly HashSet<BattlePokemon> _damagedTargets = [];

    /// <summary>
    /// The DamagingHit handlers without an order, in Showdown's collection order: the target's
    /// ability, its item (see <see cref="ItemReactionToDamagingHit"/>), then the attacker's Poison
    /// Touch. Aftermath (order 1) and Rocky Helmet (order 2) run before, for every target.
    /// </summary>
    /// <param name="moveType">The move's type as used (Aerilate, Weather Ball), which the items read.</param>
    private void AfterDamagingHit(BattlePokemon attacker, BattlePokemon target, MoveDefinition move, PokemonType moveType)
    {
        var contact = move.Flags.HasFlag(MoveFlags.Contact);
        var ability = AbilityOf(target);
        switch (ability)
        {
            // 3 in 10 against the attacker, even if the holder has just fainted.
            case "static" or "flamebody" or "poisonpoint" when contact && !attacker.IsFainted && _random.Next(0, 10) < 3:
                var status = ability switch
                {
                    "static" => MajorStatus.Paralysis,
                    "flamebody" => MajorStatus.Burn,
                    _ => MajorStatus.Poison,
                };
                SetStatus(attacker, status, target, primary: null, viaAbility: (target, ability));
                break;

            // Effect Spore spares powder-immune attackers (Grass types, Overcoat, Safety Goggles'
            // onImmunity): 11% sleep, 10% paralysis, 9% poison.
            case "effectspore" when contact && !attacker.IsFainted &&
                !_typeChart.IsImmune(attacker.Set.Types, TypeCondition.Powder) && attacker.Ability != "overcoat" &&
                attacker.Item != HeldItems.SafetyGoggles:
                var roll = _random.Next(0, 100);
                MajorStatus? spore = roll < 11 ? MajorStatus.Sleep
                    : roll < 21 ? MajorStatus.Paralysis
                    : roll < 30 ? MajorStatus.Poison
                    : null;
                if (spore is { } sporeStatus)
                {
                    SetStatus(attacker, sporeStatus, target, primary: null, viaAbility: (target, "effectspore"));
                }

                break;

            case "justified" when move.Type == PokemonType.Dark:
                ChangeStages(target, [new StatChange(BoostStat.Attack, 1)], announceLimit: false, source: target,
                    fromAbility: ability);
                break;

            case "rattled" when move.Type is PokemonType.Dark or PokemonType.Bug or PokemonType.Ghost:
                ChangeStages(target, [new StatChange(BoostStat.Speed, 1)], announceLimit: false, source: target,
                    fromAbility: ability);
                break;

            case "weakarmor" when move.Category == MoveCategory.Physical:
                ChangeStages(target, [new StatChange(BoostStat.Defense, -1), new StatChange(BoostStat.Speed, 1)],
                    announceLimit: false, source: target, fromAbility: ability);
                break;
        }

        ItemReactionToDamagingHit(target, moveType);

        // Poison Touch: the attacker's contact moves poison 3 times in 10, except through Shield Dust.
        if (attacker.Ability == "poisontouch" && contact && AbilityOf(target) != "shielddust" &&
            !target.IsFainted && _random.Next(0, 10) < 3)
        {
            SetStatus(target, MajorStatus.Poison, attacker, primary: null, viaAbility: (attacker, "poisontouch"));
        }
    }

    /// <summary>Aftermath (DamagingHit order 1) when a contact move knocked its holder out.</summary>
    private void AftermathOnDamagingHit(BattlePokemon attacker, BattlePokemon target, MoveDefinition move)
    {
        if (AbilityOf(target) == "aftermath" && move.Flags.HasFlag(MoveFlags.Contact) && target.IsFainted)
        {
            Aftermath(attacker, target);
        }
    }

    /// <summary>
    /// Aftermath: the attacker that knocked its holder out with contact loses 1/4 of its max HP,
    /// unless a Damp Pokémon is on the field or the attacker has Magic Guard.
    /// </summary>
    private void Aftermath(BattlePokemon attacker, BattlePokemon holder)
    {
        var damp = ActivePokemon().Any(pokemon => AbilityOf(pokemon) == "damp");
        if (attacker.IsFainted || damp || IgnoresIndirectDamage(attacker))
        {
            return;
        }

        AbilityActivation(holder, "aftermath");
        var (side, slot) = Locate(attacker);
        var dealt = attacker.TakeDamage(Math.Max(1, attacker.Set.Stats.Hp / 4));
        _events.Add(new AbilityDamage(side.Id, slot, attacker.Set.Species, "aftermath", dealt, attacker.CurrentHp));
        if (attacker.IsFainted)
        {
            _events.Add(new Fainted(side.Id, slot, attacker.Set.Species));
        }
    }
}
