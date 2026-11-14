using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <param name="Effectiveness">Type multiplier (0 when the target is immune).</param>
public sealed record DamageResult(int Damage, double Effectiveness, bool Critical);

/// <summary>
/// Generation 6 damage formula, following Pokémon Showdown's order of operations and rounding:
/// base damage, spread (x0.75), critical hit (x1.5), random roll (85-100%), STAB (x1.5),
/// type effectiveness (successive doublings/halvings) and a minimum of 1. Attack and defense
/// stat stages apply before the formula; critical hits ignore unfavourable stages. Burn halves
/// physical damage after effectiveness.
/// Sun and rain modify Fire and Water damage after the spread modifier, and sandstorm raises
/// the Special Defense of Rock types. Abilities and items are not applied yet.
/// </summary>
public sealed class DamageCalculator(ITypeChart typeChart)
{
    // Gen 6 critical hit odds by stage; stage 4 and above always crit.
    private static readonly int[] CritOdds = [16, 8, 2, 1];

    /// <param name="attackStage">Stage of the attacker's offensive stat used by the move (Attack or Special Attack).</param>
    /// <param name="defenseStage">Stage of the defender's defensive stat used by the move (Defense or Special Defense).</param>
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
        int finalMultiplier = 1)
    {
        var effectiveness = typeChart.Effectiveness(move.Type, defender.Types);
        if (effectiveness == 0)
        {
            return new DamageResult(0, 0, false);
        }

        if (move.LevelDamage || move.FixedDamage is not null)
        {
            return new DamageResult(move.LevelDamage ? attacker.Level : move.FixedDamage!.Value, 1, false);
        }

        var critical = IsCritical(move, random);
        var physical = move.Category == MoveCategory.Physical;
        // Critical hits ignore the attacker's drops and the defender's boosts, but keep the rest.
        if (move.IgnoreDefensive || (critical && defenseStage > 0))
        {
            defenseStage = 0;
        }

        if (critical && attackStage < 0)
        {
            attackStage = 0;
        }

        var attack = StatStages.ApplyToStat(physical ? attacker.Stats.Attack : attacker.Stats.SpecialAttack, attackStage);
        var defense = Math.Max(1, StatStages.ApplyToStat(
            physical ? defender.Stats.Defense : defender.Stats.SpecialDefense, defenseStage));

        // Sandstorm raises the Special Defense of Rock types by half, after stat stages.
        if (!physical && weather == Weather.Sandstorm && defender.Types.Contains(PokemonType.Rock))
        {
            defense = Modify(defense, 3, 2);
        }

        var damage = (2 * attacker.Level / 5 + 2) * move.BasePower * attack / defense / 50;
        damage += 2;

        if (spread)
        {
            damage = Modify(damage, 3, 4);
        }

        // Weather (Showdown's WeatherModifyDamage) applies right after the spread modifier.
        if ((weather, move.Type) is (Weather.Sun, PokemonType.Fire) or (Weather.Rain, PokemonType.Water))
        {
            damage = Modify(damage, 3, 2);
        }
        else if ((weather, move.Type) is (Weather.Sun, PokemonType.Water) or (Weather.Rain, PokemonType.Fire))
        {
            damage = Modify(damage, 1, 2);
        }

        if (critical)
        {
            damage = damage * 3 / 2;
        }

        damage = damage * random.Next(85, 101) / 100;

        if (attacker.Types.Contains(move.Type))
        {
            damage = Modify(damage, 3, 2);
        }

        for (var multiplier = effectiveness; multiplier > 1; multiplier /= 2)
        {
            damage *= 2;
        }

        for (var multiplier = effectiveness; multiplier < 1; multiplier *= 2)
        {
            damage /= 2;
        }

        // Gen 6 exempts Facade from the burn penalty.
        if (attackerBurned && physical && move.Id != "facade")
        {
            damage = Modify(damage, 1, 2);
        }

        // Final damage modifiers (Showdown's ModifyDamage), e.g. Earthquake against a digging target.
        damage *= finalMultiplier;

        return new DamageResult(Math.Max(1, damage), effectiveness, critical);
    }

    private static bool IsCritical(MoveDefinition move, IBattleRandom random)
    {
        if (move.WillCrit || move.CritRatio >= CritOdds.Length)
        {
            return true;
        }

        return random.Next(0, CritOdds[Math.Max(1, move.CritRatio) - 1]) == 0;
    }

    /// <summary>
    /// Applies a multiplier the way the games do: as a 4096-based fixed-point modifier,
    /// rounding exact halves down ("pokeRound").
    /// </summary>
    private static int Modify(int value, int numerator, int denominator)
    {
        var modifier = numerator * 4096 / denominator;
        return (int)(((long)value * modifier + 2047) / 4096);
    }
}
