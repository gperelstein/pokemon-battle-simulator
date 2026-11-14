using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Abilities inside the Gen 6 damage formula, checked against Showdown's data/abilities.ts and
/// sim/battle-actions.ts getDamage/modifyDamage. Expected values come from an independent mirror of
/// Showdown's formula. Base case: level 50, 100 Attack vs 100 Defense, 50 power: 24 (36 with STAB).
/// </summary>
public class AbilityDamageTests
{
    private static readonly DamageCalculator Calculator = new(TestTypeChart.Gen6Sample);

    private static PokemonSet Set(string? ability = null, PokemonType type = PokemonType.Normal, int hp = 300) =>
        new("Mon", [type], 50, new Stats(hp, 100, 100, 100, 100, 100), [Fixtures.Tackle], ability);

    private static MoveDefinition Move(PokemonType type, MoveCategory category = MoveCategory.Physical) =>
        new("move", "Move", type, category, 50);

    private static int Damage(Combatant attacker, Combatant defender, MoveDefinition move, Weather? weather = null) =>
        Calculator.Calculate(new DamageInput(attacker, defender, move) { Weather = weather }, new ScriptedRandom(1, 100)).Damage;

    [Theory]
    [InlineData(100, 35)] // 100 <= 300 / 3: Attack x1.5
    [InlineData(101, 24)]
    public void Blaze_boosts_fire_moves_at_a_third_of_max_hp_or_less(int currentHp, int expected)
    {
        var attacker = new Combatant(Set("blaze"), CurrentHp: currentHp);

        Assert.Equal(expected, Damage(attacker, new Combatant(Set()), Move(PokemonType.Fire)));
    }

    [Fact]
    public void Blaze_does_nothing_for_other_types()
    {
        var attacker = new Combatant(Set("blaze", PokemonType.Water), CurrentHp: 1);

        Assert.Equal(24, Damage(attacker, new Combatant(Set()), Move(PokemonType.Normal)));
    }

    [Fact]
    public void Guts_boosts_attack_when_statused_and_ignores_the_burn_penalty()
    {
        var gutsBurned = new Combatant(Set("guts"), Status: MajorStatus.Burn);
        var burned = new Combatant(Set(), Status: MajorStatus.Burn);

        Assert.Equal(52, Damage(gutsBurned, new Combatant(Set()), Move(PokemonType.Normal)));
        Assert.Equal(18, Damage(burned, new Combatant(Set()), Move(PokemonType.Normal)));
        Assert.Equal(36, Damage(new Combatant(Set("guts")), new Combatant(Set()), Move(PokemonType.Normal)));
    }

    [Fact]
    public void Hustle_boosts_physical_attack_only()
    {
        var hustle = new Combatant(Set("hustle"));

        Assert.Equal(52, Damage(hustle, new Combatant(Set()), Move(PokemonType.Normal)));
        Assert.Equal(36, Damage(hustle, new Combatant(Set()), Move(PokemonType.Normal, MoveCategory.Special)));
    }

    [Theory]
    [InlineData(PokemonType.Fire, 13)]
    [InlineData(PokemonType.Ice, 13)]
    [InlineData(PokemonType.Water, 24)]
    public void Thick_fat_halves_the_attackers_stat_against_fire_and_ice(PokemonType type, int expected) =>
        Assert.Equal(expected, Damage(new Combatant(Set()), new Combatant(Set("thickfat")), Move(type)));

    [Fact]
    public void Guts_and_thick_fat_chain_into_three_quarters()
    {
        // Chain 0.5 x 1.5 = 3072/4096: Attack 75, damage 18; burn ignored thanks to Guts.
        var attacker = new Combatant(Set("guts"), Status: MajorStatus.Burn);

        Assert.Equal(18, Damage(attacker, new Combatant(Set("thickfat")), Move(PokemonType.Fire)));
    }

    [Theory]
    [InlineData(MoveCategory.Physical, 24)] // Defense x1.5
    [InlineData(MoveCategory.Special, 36)]
    public void Marvel_scale_raises_defense_when_statused(MoveCategory category, int expected)
    {
        var defender = new Combatant(Set("marvelscale"), Status: MajorStatus.Paralysis);

        Assert.Equal(expected, Damage(new Combatant(Set()), defender, Move(PokemonType.Normal, category)));
    }

    [Fact]
    public void Marvel_scale_does_nothing_without_a_status() =>
        Assert.Equal(36, Damage(new Combatant(Set()), new Combatant(Set("marvelscale")), Move(PokemonType.Normal)));

    [Fact]
    public void Adaptability_makes_stab_double() =>
        Assert.Equal(48, Damage(new Combatant(Set("adaptability")), new Combatant(Set()), Move(PokemonType.Normal)));

    [Theory]
    [InlineData("battlearmor")]
    [InlineData("shellarmor")]
    public void Battle_armor_and_shell_armor_block_critical_hits(string ability)
    {
        var stormThrow = Move(PokemonType.Normal) with { WillCrit = true };

        var result = Calculator.Calculate(
            new DamageInput(new Combatant(Set()), new Combatant(Set(ability)), stormThrow), new ScriptedRandom(100));

        Assert.Equal(new DamageResult(36, 1, false), result);
    }

    [Fact]
    public void Tinted_lens_doubles_not_very_effective_damage()
    {
        var waterTarget = new Combatant(Set(type: PokemonType.Water));

        Assert.Equal(24, Damage(new Combatant(Set("tintedlens")), waterTarget, Move(PokemonType.Fire)));
        Assert.Equal(12, Damage(new Combatant(Set()), waterTarget, Move(PokemonType.Fire)));
    }

    [Fact]
    public void Sniper_boosts_critical_hits_by_half()
    {
        var stormThrow = Move(PokemonType.Normal) with { WillCrit = true };

        var sniper = Calculator.Calculate(
            new DamageInput(new Combatant(Set("sniper")), new Combatant(Set()), stormThrow), new ScriptedRandom(100));

        Assert.Equal(81, sniper.Damage); // 54 x1.5
    }

    [Fact]
    public void Filter_softens_super_effective_hits()
    {
        var fireTarget = Set("filter", PokemonType.Fire);

        Assert.Equal(36, Damage(new Combatant(Set()), new Combatant(fireTarget), Move(PokemonType.Water))); // 48 x0.75
        Assert.Equal(48, Damage(new Combatant(Set()), new Combatant(Set(type: PokemonType.Fire)), Move(PokemonType.Water)));
    }

    [Theory]
    [InlineData(300, 18)]
    [InlineData(299, 36)]
    public void Multiscale_halves_damage_at_full_hp(int currentHp, int expected) =>
        Assert.Equal(
            expected,
            Damage(new Combatant(Set()), new Combatant(Set("multiscale"), CurrentHp: currentHp), Move(PokemonType.Normal)));

    [Fact]
    public void Unaware_ignores_the_other_pokemons_stages()
    {
        var boostedDefender = new Combatant(Set(), DefenseStage: 2);
        var boostedAttacker = new Combatant(Set(), AttackStage: 2);

        Assert.Equal(36, Damage(new Combatant(Set("unaware")), boostedDefender, Move(PokemonType.Normal)));
        Assert.Equal(19, Damage(new Combatant(Set()), boostedDefender, Move(PokemonType.Normal)));
        Assert.Equal(36, Damage(boostedAttacker, new Combatant(Set("unaware")), Move(PokemonType.Normal)));
        Assert.Equal(69, Damage(boostedAttacker, new Combatant(Set()), Move(PokemonType.Normal)));
    }

    [Fact]
    public void Scrappy_hits_ghosts_with_normal_and_fighting_moves()
    {
        var ghost = new Combatant(Set(type: PokemonType.Ghost));

        Assert.Equal(36, Damage(new Combatant(Set("scrappy")), ghost, Move(PokemonType.Normal)));
        Assert.Equal(24, Damage(new Combatant(Set("scrappy")), ghost, Move(PokemonType.Fighting)));
        Assert.Equal(0, Damage(new Combatant(Set()), ghost, Move(PokemonType.Normal)));
    }

    [Theory]
    [InlineData(Weather.Sun, 52)]
    [InlineData(null, 36)]
    public void Solar_power_boosts_special_attack_in_sun(Weather? weather, int expected) =>
        Assert.Equal(
            expected,
            Damage(new Combatant(Set("solarpower")), new Combatant(Set()), Move(PokemonType.Normal, MoveCategory.Special), weather));
}
