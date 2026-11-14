using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <param name="Effectiveness">Type multiplier (0 when the target is immune).</param>
public sealed record DamageResult(int Damage, double Effectiveness, bool Critical);

/// <summary>A Pokémon's battle state as far as the damage formula is concerned.</summary>
/// <param name="CurrentHp">Current HP; null means full HP.</param>
/// <param name="AttackStage">Stage of the offensive stat the move uses (Attack or Special Attack).</param>
/// <param name="DefenseStage">Stage of the defensive stat the move uses (Defense or Special Defense).</param>
/// <param name="AbilitySuppressed">Its ability is ignored (for instance by Mold Breaker).</param>
/// <param name="FlashFire">Flash Fire has been activated: its Fire moves use x1.5 Attack or Special Attack.</param>
/// <param name="ConsecutiveUses">Consecutive uses counted by a held Metronome (x1.2 per use, up to x2).</param>
public sealed record Combatant(
    PokemonSet Set,
    int? CurrentHp = null,
    MajorStatus? Status = null,
    int AttackStage = 0,
    int DefenseStage = 0,
    bool AbilitySuppressed = false,
    bool FlashFire = false,
    int ConsecutiveUses = 0)
{
    public int Hp => CurrentHp ?? Set.Stats.Hp;
    public string? Ability => AbilitySuppressed ? null : Set.Ability;
}

/// <summary>Everything one hit of a move needs from the battle.</summary>
public sealed record DamageInput(Combatant Attacker, Combatant Defender, MoveDefinition Move)
{
    /// <summary>The move hits more than one target (x0.75).</summary>
    public bool Spread { get; init; }

    public Weather? Weather { get; init; }

    /// <summary>
    /// Final damage modifiers decided by the battle (Showdown's ModifyDamage handlers outside the two
    /// Pokémon's abilities): Earthquake against a digging target, an ally's Friend Guard...
    /// </summary>
    public ModifierChain FinalModifiers { get; init; }
}

/// <summary>
/// Generation 6 damage formula, following Pokémon Showdown's getDamage/modifyDamage order and
/// rounding: stat stages (critical hits ignore unfavourable ones), stat modifiers (Guts, Thick Fat,
/// Marvel Scale, sandstorm Rock Special Defense...), base damage, spread (x0.75), weather, critical
/// hit (x1.5), random roll (85-100%), STAB (x1.5, x2 with Adaptability), type effectiveness, burn
/// (x0.5, not for Guts or Facade), final modifiers (Tinted Lens, Sniper, Filter, Multiscale...)
/// and a minimum of 1. Modifiers on the same stat are chained like Showdown's chainModify. Held
/// items (<see cref="HeldItems"/>) add their stat, critical hit and final damage modifiers.
/// </summary>
public sealed class DamageCalculator(ITypeChart typeChart)
{
    // Gen 6 critical hit odds by stage; stage 4 and above always crit.
    private static readonly int[] CritOdds = [16, 8, 2, 1];

    /// <summary>Shortcut for a hit between two Pokémon at full HP without abilities in play.</summary>
    public DamageResult Calculate(
        PokemonSet attacker,
        PokemonSet defender,
        MoveDefinition move,
        bool spread,
        IBattleRandom random,
        int attackStage = 0,
        int defenseStage = 0,
        bool attackerBurned = false,
        Weather? weather = null,
        int finalMultiplier = 1) =>
        Calculate(
            new DamageInput(
                new Combatant(attacker, Status: attackerBurned ? MajorStatus.Burn : null, AttackStage: attackStage),
                new Combatant(defender, DefenseStage: defenseStage),
                move)
            {
                Spread = spread,
                Weather = weather,
                FinalModifiers = new ModifierChain().Chain(finalMultiplier),
            },
            random);

    public DamageResult Calculate(DamageInput input, IBattleRandom random)
    {
        var (attacker, defender, move) = (input.Attacker, input.Defender, input.Move);
        var effectiveness = Effectiveness(attacker, defender, move);
        if (effectiveness == 0)
        {
            return new DamageResult(0, 0, false);
        }

        if (move.LevelDamage || move.FixedDamage is not null)
        {
            return new DamageResult(move.LevelDamage ? attacker.Set.Level : move.FixedDamage!.Value, 1, false);
        }

        // The roll happens even against Battle Armor / Shell Armor, which then cancel the crit.
        var critical = IsCritical(move, attacker.Set, random) && defender.Ability is not ("battlearmor" or "shellarmor");
        var physical = move.Category == MoveCategory.Physical;

        var attack = AttackStat(input, critical, physical);
        var defense = DefenseStat(input, critical, physical);

        var damage = (2 * attacker.Set.Level / 5 + 2) * move.BasePower * attack / defense / 50;
        damage += 2;

        if (input.Spread)
        {
            damage = Modify(damage, 3, 4);
        }

        // Weather (Showdown's WeatherModifyDamage) applies right after the spread modifier.
        if ((input.Weather, move.Type) is (Weather.Sun, PokemonType.Fire) or (Weather.Rain, PokemonType.Water))
        {
            damage = Modify(damage, 3, 2);
        }
        else if ((input.Weather, move.Type) is (Weather.Sun, PokemonType.Water) or (Weather.Rain, PokemonType.Fire))
        {
            damage = Modify(damage, 1, 2);
        }

        if (critical)
        {
            damage = damage * 3 / 2;
        }

        damage = damage * random.Next(85, 101) / 100;

        if (attacker.Set.Types.Contains(move.Type))
        {
            damage = attacker.Ability == "adaptability" ? Modify(damage, 2, 1) : Modify(damage, 3, 2);
        }

        for (var multiplier = effectiveness; multiplier > 1; multiplier /= 2)
        {
            damage *= 2;
        }

        for (var multiplier = effectiveness; multiplier < 1; multiplier *= 2)
        {
            damage /= 2;
        }

        // Gen 6 exempts Facade from the burn penalty; Guts ignores it.
        if (attacker.Status == MajorStatus.Burn && physical && move.Id != "facade" && attacker.Ability != "guts")
        {
            damage = Modify(damage, 1, 2);
        }

        damage = FinalModifiers(input, effectiveness, critical).Apply(damage);
        return new DamageResult(Math.Max(1, damage), effectiveness, critical);
    }

    /// <summary>
    /// Scrappy lets Normal and Fighting moves ignore Ghost immunity (neutral against it); an Iron Ball
    /// holder's Flying type does not count against Ground moves.
    /// </summary>
    private double Effectiveness(Combatant attacker, Combatant defender, MoveDefinition move)
    {
        var types = HeldItems.DefendingTypes(defender.Set, move.Type);
        if (attacker.Ability == "scrappy" && move.Type is PokemonType.Normal or PokemonType.Fighting)
        {
            return types
                .Select(type => typeChart.Effectiveness(move.Type, [type]))
                .Aggregate(1.0, (total, multiplier) => total * (multiplier == 0 ? 1 : multiplier));
        }

        return typeChart.Effectiveness(move.Type, types);
    }

    private static int AttackStat(DamageInput input, bool critical, bool physical)
    {
        var (attacker, defender, move) = (input.Attacker, input.Defender, input.Move);
        var stage = attacker.AttackStage;
        // Critical hits ignore the attacker's drops; Unaware ignores all of the attacker's stages.
        if ((critical && stage < 0) || defender.Ability == "unaware")
        {
            stage = 0;
        }

        var attack = StatStages.ApplyToStat(physical ? attacker.Set.Stats.Attack : attacker.Set.Stats.SpecialAttack, stage);
        if (physical && attacker.Ability == "hustle")
        {
            attack = Modify(attack, 3, 2);
        }

        // ModifyAtk/ModifySpA: Thick Fat (defender, priority 6) before the attacker's own abilities (5).
        var chain = new ModifierChain();
        if (defender.Ability == "thickfat" && move.Type is PokemonType.Fire or PokemonType.Ice)
        {
            chain = chain.Chain(1, 2);
        }

        var pinch = attacker.Hp * 3 <= attacker.Set.Stats.Hp;
        var boosted = attacker.Ability switch
        {
            "blaze" => pinch && move.Type == PokemonType.Fire,
            "overgrow" => pinch && move.Type == PokemonType.Grass,
            "torrent" => pinch && move.Type == PokemonType.Water,
            "swarm" => pinch && move.Type == PokemonType.Bug,
            "guts" => physical && attacker.Status is not null,
            "solarpower" => !physical && input.Weather == Weather.Sun,
            "flashfire" => attacker.FlashFire && move.Type == PokemonType.Fire,
            _ => false,
        };
        if (boosted)
        {
            chain = chain.Chain(3, 2);
        }

        // ...then the attacker's item (priority 1).
        return HeldItems.ChainAttack(chain, attacker.Set, physical).Apply(attack);
    }

    private static int DefenseStat(DamageInput input, bool critical, bool physical)
    {
        var (attacker, defender, move) = (input.Attacker, input.Defender, input.Move);
        var stage = defender.DefenseStage;
        // Critical hits ignore the defender's boosts; Chip Away and Unaware ignore all its stages.
        if (move.IgnoreDefensive || (critical && stage > 0) || attacker.Ability == "unaware")
        {
            stage = 0;
        }

        var defense = StatStages.ApplyToStat(physical ? defender.Set.Stats.Defense : defender.Set.Stats.SpecialDefense, stage);

        // Sandstorm raises the Special Defense of Rock types by half, after stat stages.
        if (!physical && input.Weather == Weather.Sandstorm && defender.Set.Types.Contains(PokemonType.Rock))
        {
            defense = Modify(defense, 3, 2);
        }

        // ModifyDef/ModifySpD chain: Marvel Scale (priority 6), then the defender's item (2, 1).
        var chain = new ModifierChain();
        if (physical && defender.Ability == "marvelscale" && defender.Status is not null)
        {
            chain = chain.Chain(3, 2);
        }

        return Math.Max(1, HeldItems.ChainDefense(chain, defender.Set, physical).Apply(defense));
    }

    /// <summary>
    /// Showdown's ModifyDamage handlers: the attacker's (its Metronome condition, then its abilities,
    /// then its item, following Showdown's subOrder), the defender's and the battle's.
    /// </summary>
    private static ModifierChain FinalModifiers(DamageInput input, double effectiveness, bool critical)
    {
        var chain = HeldItems.ChainMetronome(new ModifierChain(), input.Attacker.Set, input.Attacker.ConsecutiveUses);
        if (input.Attacker.Ability == "tintedlens" && effectiveness < 1)
        {
            chain = chain.Chain(2);
        }

        if (input.Attacker.Ability == "sniper" && critical)
        {
            chain = chain.Chain(3, 2);
        }

        chain = HeldItems.ChainDamage(chain, input.Attacker.Set, effectiveness);

        if (input.Defender.Ability == "filter" && effectiveness > 1)
        {
            chain = chain.Chain(3, 4);
        }

        if (input.Defender.Ability == "multiscale" && input.Defender.Hp >= input.Defender.Set.Stats.Hp)
        {
            chain = chain.Chain(1, 2);
        }

        return chain.Chain(input.FinalModifiers.Modifier, 4096);
    }

    /// <summary>Critical hit roll; the attacker's item can raise the stage (ModifyCritRatio).</summary>
    private static bool IsCritical(MoveDefinition move, PokemonSet attacker, IBattleRandom random)
    {
        var stage = move.CritRatio + HeldItems.CritStageBonus(attacker);
        if (move.WillCrit || stage >= CritOdds.Length)
        {
            return true;
        }

        return random.Next(0, CritOdds[Math.Max(1, stage) - 1]) == 0;
    }

    /// <summary>
    /// Applies a multiplier the way the games do: as a 4096-based fixed-point modifier,
    /// rounding exact halves down ("pokeRound").
    /// </summary>
    private static int Modify(int value, int numerator, int denominator) =>
        new ModifierChain().Chain(numerator, denominator).Apply(value);
}
