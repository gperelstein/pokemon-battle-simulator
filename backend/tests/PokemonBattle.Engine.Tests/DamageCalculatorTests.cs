using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Tests;

public class DamageCalculatorTests
{
    private static readonly DamageCalculator Calculator = new(TestTypeChart.Gen6Sample);

    private static PokemonSet Set(PokemonType type, int level = 50, int attack = 100, int defense = 100) =>
        new("Mon", [type], level, new Stats(300, attack, defense, attack, defense, 100), [Fixtures.Tackle]);

    private static MoveDefinition Move(PokemonType type, int power, MoveCategory category = MoveCategory.Physical) =>
        new("move", "Move", type, category, power);

    [Fact]
    public void Applies_base_formula_and_stab()
    {
        // floor(floor(22 * 50 * 100 / 100) / 50) = 22, +2 = 24, max roll = 24, STAB 24 * 1.5 = 36.
        var result = Calculator.Calculate(
            Set(PokemonType.Normal), Set(PokemonType.Normal), Move(PokemonType.Normal, 50), false, new ScriptedRandom(1, 100));

        Assert.Equal(new DamageResult(36, 1, false), result);
    }

    [Fact]
    public void Chains_spread_crit_roll_stab_and_effectiveness_with_gen_6_rounding()
    {
        // Base: floor(floor(42 * 90 * 300 / 200) / 50) = 113, +2 = 115.
        // Spread: pokeRound(115 * 0.75) = 86. Crit: floor(86 * 1.5) = 129. Roll 85: floor(129 * 0.85) = 109.
        // STAB: 109 * 1.5 = 163.5 rounds half down to 163. Super effective: 326.
        var attacker = Set(PokemonType.Water, level: 100, attack: 300);
        var defender = Set(PokemonType.Fire, defense: 200);
        var surf = Move(PokemonType.Water, 90, MoveCategory.Special) with { WillCrit = true };

        var result = Calculator.Calculate(attacker, defender, surf, spread: true, new ScriptedRandom(85));

        Assert.Equal(new DamageResult(326, 2, true), result);
    }

    [Fact]
    public void Applies_effectiveness_as_successive_halvings()
    {
        // 24 -> 12 -> 6 against a double resistance.
        var defender = Set(PokemonType.Water) with { Types = [PokemonType.Water, PokemonType.Dragon] };

        var result = Calculator.Calculate(
            Set(PokemonType.Normal), defender, Move(PokemonType.Fire, 50), false, new ScriptedRandom(1, 100));

        Assert.Equal(new DamageResult(6, 0.25, false), result);
    }

    [Fact]
    public void Deals_no_damage_to_immune_targets()
    {
        var result = Calculator.Calculate(
            Set(PokemonType.Electric), Set(PokemonType.Ground), Move(PokemonType.Electric, 90), false, new ScriptedRandom(1, 100));

        Assert.Equal(new DamageResult(0, 0, false), result);
    }

    [Fact]
    public void Never_deals_less_than_one_damage_to_a_non_immune_target()
    {
        var weak = Set(PokemonType.Normal, level: 1, attack: 1);
        var wall = Set(PokemonType.Water, defense: 999) with { Types = [PokemonType.Water, PokemonType.Dragon] };

        var result = Calculator.Calculate(weak, wall, Move(PokemonType.Fire, 10), false, new ScriptedRandom(1, 85));

        Assert.Equal(1, result.Damage);
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(2, 8)]
    [InlineData(3, 2)]
    public void Crit_chance_follows_the_gen_6_stages(int critRatio, int denominator)
    {
        var random = new ScriptedRandom(0, 100);
        var move = Move(PokemonType.Normal, 50) with { CritRatio = critRatio };

        var result = Calculator.Calculate(Set(PokemonType.Normal), Set(PokemonType.Normal), move, false, random);

        Assert.True(result.Critical);
        Assert.Equal((0, denominator), random.Calls[0]);
    }

    [Fact]
    public void Crit_ratio_of_four_or_more_always_crits()
    {
        var move = Move(PokemonType.Normal, 50) with { CritRatio = 4 };

        var result = Calculator.Calculate(Set(PokemonType.Normal), Set(PokemonType.Normal), move, false, new ScriptedRandom(100));

        Assert.True(result.Critical);
    }

    [Fact]
    public void Level_based_fixed_damage_ignores_stats_and_effectiveness()
    {
        var seismicToss = Move(PokemonType.Fighting, 0) with { LevelDamage = true };

        var result = Calculator.Calculate(
            Set(PokemonType.Normal, level: 77), Set(PokemonType.Rock), seismicToss, false, new ScriptedRandom());

        Assert.Equal(new DamageResult(77, 1, false), result);
    }

    [Fact]
    public void Fixed_damage_still_respects_type_immunity()
    {
        var seismicToss = Move(PokemonType.Fighting, 0) with { LevelDamage = true };
        var dragonRage = Move(PokemonType.Dragon, 0, MoveCategory.Special) with { FixedDamage = 40 };

        Assert.Equal(0, Calculator.Calculate(Set(PokemonType.Normal), Set(PokemonType.Ghost), seismicToss, false, new ScriptedRandom()).Damage);
        Assert.Equal(40, Calculator.Calculate(Set(PokemonType.Normal), Set(PokemonType.Water), dragonRage, false, new ScriptedRandom()).Damage);
    }

    [Fact]
    public void Identifies_moves_whose_power_needs_custom_logic()
    {
        Assert.True(Move(PokemonType.Fighting, 0).HasVariablePower);
        Assert.False((Move(PokemonType.Fighting, 0) with { LevelDamage = true }).HasVariablePower);
        Assert.False(Move(PokemonType.Normal, 0, MoveCategory.Status).HasVariablePower);
        Assert.False(Move(PokemonType.Normal, 40).HasVariablePower);
    }
}
