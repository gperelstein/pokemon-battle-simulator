using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <param name="Effectiveness">Type multiplier (0 when the target is immune).</param>
public sealed record DamageResult(int Damage, double Effectiveness, bool Critical);

/// <summary>
/// Generation 6 damage formula, following Pokémon Showdown's order of operations and rounding:
/// base damage, spread (x0.75), critical hit (x1.5), random roll (85-100%), STAB (x1.5),
/// type effectiveness (successive doublings/halvings) and a minimum of 1.
/// Stat stages, weather, burn, abilities and items are not applied yet.
/// </summary>
public sealed class DamageCalculator(ITypeChart typeChart)
{
    // Gen 6 critical hit odds by stage; stage 4 and above always crit.
    private static readonly int[] CritOdds = [16, 8, 2, 1];

    public DamageResult Calculate(
        PokemonSet attacker, PokemonSet defender, MoveDefinition move, bool spread, IBattleRandom random)
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
        var attack = physical ? attacker.Stats.Attack : attacker.Stats.SpecialAttack;
        var defense = Math.Max(1, physical ? defender.Stats.Defense : defender.Stats.SpecialDefense);

        var damage = (2 * attacker.Level / 5 + 2) * move.BasePower * attack / defense / 50;
        damage += 2;

        if (spread)
        {
            damage = Modify(damage, 3, 4);
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
