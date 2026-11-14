using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Abilities triggered by entering or leaving the field and at the end of the turn, following
/// Showdown's data/abilities.ts (Gen 6) and sim/battle-actions.ts: Pokémon that switch in together
/// (the leads, end-of-turn replacements) resolve their abilities by speed once all of them are in.
/// </summary>
public sealed partial class Battle
{
    private static readonly HashSet<string> RetaliationMoves = ["counter", "metalburst", "mirrorcoat"];

    /// <summary>
    /// The set as the damage formula must see it: with the current (possibly traced) ability and the
    /// current held item (none once used up).
    /// </summary>
    private static PokemonSet CombatSet(BattlePokemon pokemon) =>
        pokemon.Ability == pokemon.Set.Ability && pokemon.Item == pokemon.Set.Item
            ? pokemon.Set
            : pokemon.Set with
            {
                Ability = pokemon.Ability, AbilityBreakable = pokemon.AbilityBreakable, Item = pokemon.Item,
            };

    private IEnumerable<BattlePokemon> ActivePokemon() =>
        _sides.Values.SelectMany(side => side.LiveSlots.Select(slot => side.Active[slot]!));

    private IEnumerable<BattlePokemon> FoesOf(BattlePokemon pokemon)
    {
        var foes = _sides[pokemon.Side.Opponent()];
        return foes.LiveSlots.Select(slot => foes.Active[slot]!);
    }

    /// <summary>Runs the switch-in abilities of Pokémon that just entered, fastest first.</summary>
    private void RunSwitchInAbilities(IEnumerable<BattlePokemon> switchedIn)
    {
        foreach (var pokemon in switchedIn.OrderByDescending(p => p.Speed).ToArray())
        {
            if (!pokemon.IsFainted && Locate(pokemon).Slot >= 0)
            {
                StartAbility(pokemon);
            }
        }

        UpdateAbilities();
    }

    /// <summary>An ability's start effect (Showdown's onStart / onSwitchIn).</summary>
    private void StartAbility(BattlePokemon pokemon)
    {
        switch (pokemon.Ability)
        {
            case "intimidate":
                var foes = FoesOf(pokemon).ToArray();
                if (foes.Length == 0)
                {
                    return;
                }

                AbilityActivation(pokemon, "intimidate");
                foreach (var foe in foes)
                {
                    ChangeStages(foe, [new StatChange(BoostStat.Attack, -1)], announceLimit: true, source: pokemon);
                }

                return;

            case "drought" when Weather != Battles.Weather.Sun:
                AbilityActivation(pokemon, "drought");
                SetWeather(Battles.Weather.Sun);
                return;

            case "download":
                // Compares the foes' total Defense and Special Defense, stages included; a tie gives Sp. Atk.
                var defense = FoesOf(pokemon).Sum(foe =>
                    StatStages.ApplyToStat(foe.Set.Stats.Defense, foe.GetStage(BoostStat.Defense)));
                var specialDefense = FoesOf(pokemon).Sum(foe =>
                    StatStages.ApplyToStat(foe.Set.Stats.SpecialDefense, foe.GetStage(BoostStat.SpecialDefense)));
                var raised = defense > 0 && defense >= specialDefense ? BoostStat.SpecialAttack
                    : specialDefense > 0 ? BoostStat.Attack
                    : (BoostStat?)null;
                if (raised is { } stat)
                {
                    AbilityActivation(pokemon, "download");
                    ChangeStages(pokemon, [new StatChange(stat, 1)], announceLimit: false, source: pokemon);
                }

                return;

            case "cloudnine" or "moldbreaker":
                AbilityActivation(pokemon, pokemon.Ability);
                return;

            case "anticipation":
                var shudders = FoesOf(pokemon).SelectMany(foe => foe.Set.Moves).Any(move =>
                    move.Category != MoveCategory.Status &&
                    (move.Ohko || _typeChart.Effectiveness(move.Type, pokemon.Set.Types) > 1));
                if (shudders)
                {
                    AbilityActivation(pokemon, "anticipation");
                }

                return;

            case "forewarn":
                Forewarn(pokemon);
                return;

            case "trace":
                TryTrace(pokemon);
                return;
        }
    }

    /// <summary>
    /// Forewarn reveals the foes' strongest move: OHKO moves count as 150, Counter, Mirror Coat and
    /// Metal Burst as 120, other damaging moves without a fixed power as 80; ties are picked at random.
    /// </summary>
    private void Forewarn(BattlePokemon pokemon)
    {
        var strongest = new List<(BattlePokemon Foe, MoveDefinition Move)>();
        var best = 1;
        foreach (var foe in FoesOf(pokemon))
        {
            foreach (var move in foe.Set.Moves)
            {
                var power = move.Ohko ? 150
                    : RetaliationMoves.Contains(move.Id) ? 120
                    : move.Category != MoveCategory.Status && move.BasePower <= 1 ? 80
                    : move.BasePower;
                if (power > best)
                {
                    best = power;
                    strongest.Clear();
                }

                if (power == best)
                {
                    strongest.Add((foe, move));
                }
            }
        }

        if (strongest.Count == 0)
        {
            return;
        }

        var (warned, warnedMove) = strongest[_random.Next(0, strongest.Count)];
        var (side, slot) = Locate(pokemon);
        var (foeSide, foeSlot) = Locate(warned);
        _events.Add(new ForewarnAlert(side.Id, slot, pokemon.Set.Species, warnedMove.Id, foeSide.Id, foeSlot));
    }

    /// <summary>
    /// Trace copies the ability of a random foe whose ability can be traced, then runs it; with no
    /// copyable foe it keeps looking whenever the field changes.
    /// </summary>
    private void TryTrace(BattlePokemon pokemon)
    {
        var candidates = FoesOf(pokemon).Where(foe => foe.Ability is not null && foe.AbilityTraceable).ToArray();
        if (candidates.Length == 0)
        {
            pokemon.TraceSeeking = true;
            return;
        }

        var traced = candidates[_random.Next(0, candidates.Length)];
        pokemon.TraceSeeking = false;
        pokemon.ChangeAbility(traced.Ability, traced.AbilityBreakable, traced.AbilityTraceable);
        var (side, slot) = Locate(pokemon);
        var (foeSide, foeSlot) = Locate(traced);
        _events.Add(new AbilityTraced(side.Id, slot, pokemon.Set.Species, traced.Ability!, foeSide.Id, foeSlot));
        // The catalog marks Trace itself as untraceable; never chain Trace into Trace regardless.
        if (pokemon.Ability != "trace")
        {
            StartAbility(pokemon);
        }
    }

    /// <summary>
    /// Showdown's Update event, run after every action: a Pokémon whose ability blocks its current
    /// status cures it (for example after being put to sleep through Mold Breaker), and Trace keeps
    /// looking for an ability to copy.
    /// </summary>
    private void UpdateAbilities()
    {
        foreach (var pokemon in ActivePokemon().ToArray())
        {
            if (pokemon.Status is { } status && pokemon.Ability is not "leafguard" &&
                StatusBlockingAbility(pokemon, status) is { } ability)
            {
                AbilityActivation(pokemon, ability);
                Cure(pokemon);
            }

            if (pokemon.TraceSeeking)
            {
                TryTrace(pokemon);
            }
        }
    }

    /// <summary>Natural Cure and Regenerator act when a Pokémon that has not fainted leaves the field.</summary>
    private void RunSwitchOutAbilities(BattlePokemon pokemon)
    {
        if (pokemon.IsFainted)
        {
            return;
        }

        switch (pokemon.Ability)
        {
            case "naturalcure" when pokemon.Status is not null:
                Cure(pokemon);
                break;

            case "regenerator":
                var (side, slot) = Locate(pokemon);
                var healed = pokemon.Heal(pokemon.Set.Stats.Hp / 3);
                if (healed > 0)
                {
                    _events.Add(new Healed(side.Id, slot, pokemon.Set.Species, healed, pokemon.CurrentHp));
                }

                break;
        }
    }

    /// <summary>
    /// End-of-turn abilities at residual order 5 (after weather, before poison and burn), by speed:
    /// Hydration cures in rain, Shed Skin cures one time in three (33%), Healer cures each statused
    /// ally three times in ten.
    /// </summary>
    private void ApplyAbilityResiduals()
    {
        foreach (var pokemon in ActivePokemon().OrderByDescending(p => p.Speed).ToArray())
        {
            if (pokemon.IsFainted)
            {
                continue;
            }

            switch (pokemon.Ability)
            {
                case "hydration" when pokemon.Status is not null && EffectiveWeather == Battles.Weather.Rain:
                case "shedskin" when pokemon.Status is not null && _random.Next(0, 100) < 33:
                    AbilityActivation(pokemon, pokemon.Ability);
                    Cure(pokemon);
                    break;

                case "healer":
                    var side = _sides[pokemon.Side];
                    var allies = side.LiveSlots.Select(slot => side.Active[slot]!)
                        .Where(ally => ally != pokemon && ally.Status is not null)
                        .ToArray();
                    foreach (var ally in allies.Where(_ => _random.Next(0, 10) < 3))
                    {
                        AbilityActivation(pokemon, "healer");
                        Cure(ally);
                    }

                    break;
            }
        }
    }
}
