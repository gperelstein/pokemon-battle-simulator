using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Passive held items inside the Gen 6 damage formula, checked against Showdown's data/items.ts
/// (no Gen 6 overrides for these handlers) and sim/battle-actions.ts getDamage/modifyDamage.
/// Expected values come from an independent mirror of Showdown's formula. Base case: level 50,
/// 100 Attack vs 100 Defense, 50 power, no STAB, max roll: 24.
/// </summary>
public class HeldItemDamageTests
{
    private static readonly DamageCalculator Calculator = new(TestTypeChart.Gen6Sample);

    private static PokemonSet Set(string? item = null, string species = "Mon", PokemonType type = PokemonType.Normal) =>
        new(species, [type], 50, new Stats(300, 100, 100, 100, 100, 100), [Fixtures.Tackle], Item: item);

    private static MoveDefinition Move(
        MoveCategory category = MoveCategory.Physical, PokemonType type = PokemonType.Fire, int critRatio = 1) =>
        new("move", "Move", type, category, 50, CritRatio: critRatio);

    private static int Damage(PokemonSet attacker, PokemonSet defender, MoveDefinition move) =>
        Calculator.Calculate(new DamageInput(new Combatant(attacker), new Combatant(defender), move), new ScriptedRandom(1, 100))
            .Damage;

    [Theory]
    [InlineData(null)]
    [InlineData("leftovers")] // no passive damage effect
    public void Sets_without_a_damage_item_keep_their_damage(string? item)
    {
        Assert.Equal(24, Damage(Set(item), Set(item), Move()));
        Assert.Equal(36, Damage(Set(item), Set(item), Move(type: PokemonType.Normal))); // STAB
    }

    [Theory]
    [InlineData("choiceband", MoveCategory.Physical, 35)] // Attack 150
    [InlineData("choiceband", MoveCategory.Special, 24)]
    [InlineData("choicespecs", MoveCategory.Special, 35)] // Special Attack 150
    [InlineData("choicespecs", MoveCategory.Physical, 24)]
    public void Choice_band_and_specs_boost_their_stat(string item, MoveCategory category, int expected) =>
        Assert.Equal(expected, Damage(Set(item), Set(), Move(category)));

    [Fact]
    public void Choice_band_chains_with_guts()
    {
        // ModifyAtk chain: Guts (priority 5) x1.5 then Choice Band (1) x1.5 = 9216/4096: Attack 225.
        var attacker = new Combatant(Set("choiceband") with { Ability = "guts" }, Status: MajorStatus.Paralysis);

        Assert.Equal(51, Calculator.Calculate(
            new DamageInput(attacker, new Combatant(Set()), Move()), new ScriptedRandom(1, 100)).Damage);
    }

    [Theory]
    [InlineData("Pikachu", null, MoveCategory.Physical, 46)] // Attack x2
    [InlineData("Pikachu", null, MoveCategory.Special, 46)] // Special Attack x2
    [InlineData("Pikachu-Cosplay", "Pikachu", MoveCategory.Special, 46)] // every Pikachu forme
    [InlineData("Raichu", null, MoveCategory.Physical, 24)]
    public void Light_ball_doubles_pikachus_attacking_stats(string species, string? baseSpecies, MoveCategory category, int expected)
    {
        var attacker = Set("lightball", species) with { BaseSpecies = baseSpecies };

        Assert.Equal(expected, Damage(attacker, Set(), Move(category)));
    }

    [Theory]
    [InlineData("Cubone", MoveCategory.Physical, 46)]
    [InlineData("Marowak", MoveCategory.Physical, 46)]
    [InlineData("Marowak", MoveCategory.Special, 24)] // Attack only
    [InlineData("Mon", MoveCategory.Physical, 24)]
    public void Thick_club_doubles_cubone_and_marowaks_attack(string species, MoveCategory category, int expected) =>
        Assert.Equal(expected, Damage(Set("thickclub", species), Set(), Move(category)));

    [Theory]
    [InlineData(true, MoveCategory.Physical, 16)] // Defense 150
    [InlineData(true, MoveCategory.Special, 16)] // Special Defense 150
    [InlineData(false, MoveCategory.Physical, 24)]
    public void Eviolite_boosts_both_defenses_of_a_pokemon_that_can_evolve(bool canEvolve, MoveCategory category, int expected) =>
        Assert.Equal(expected, Damage(Set(), Set("eviolite") with { CanEvolve = canEvolve }, Move(category)));

    [Theory]
    [InlineData("Ditto", MoveCategory.Physical, 13)] // Defense 200
    [InlineData("Ditto", MoveCategory.Special, 24)]
    [InlineData("Mon", MoveCategory.Physical, 24)]
    public void Metal_powder_doubles_dittos_defense(string species, MoveCategory category, int expected) =>
        Assert.Equal(expected, Damage(Set(), Set("metalpowder", species), Move(category)));

    [Theory]
    [InlineData(MoveCategory.Special, 16)] // Special Defense 150
    [InlineData(MoveCategory.Physical, 24)]
    public void Assault_vest_boosts_special_defense(MoveCategory category, int expected) =>
        Assert.Equal(expected, Damage(Set(), Set("assaultvest"), Move(category)));

    [Theory]
    [InlineData(PokemonType.Water, 58)] // super effective on Fire: 48 x 4915/4096
    [InlineData(PokemonType.Fire, 24)] // neutral (not in the sample chart)
    public void Expert_belt_boosts_super_effective_hits(PokemonType moveType, int expected) =>
        Assert.Equal(expected, Damage(Set("expertbelt"), Set(type: PokemonType.Fire), Move(type: moveType)));

    [Fact]
    public void Life_orb_boosts_damage_by_5324_4096() =>
        Assert.Equal(31, Damage(Set("lifeorb"), Set(), Move()));

    [Theory]
    [InlineData(0, 24)]
    [InlineData(1, 29)] // 4915/4096
    [InlineData(2, 34)] // 5734/4096
    [InlineData(3, 38)] // 6553/4096
    [InlineData(4, 43)] // 7372/4096
    [InlineData(5, 48)] // 8192/4096
    [InlineData(9, 48)] // capped at 5 consecutive uses
    public void Metronome_boosts_damage_per_consecutive_use(int uses, int expected)
    {
        var attacker = new Combatant(Set("metronome"), ConsecutiveUses: uses);

        Assert.Equal(expected, Calculator.Calculate(
            new DamageInput(attacker, new Combatant(Set()), Move()), new ScriptedRandom(1, 100)).Damage);
    }

    [Theory]
    [InlineData("scopelens", "Mon", 1, 8)] // stage 2: 1 in 8
    [InlineData("razorclaw", "Mon", 1, 8)]
    [InlineData("scopelens", "Mon", 2, 2)] // stage 3: 1 in 2
    [InlineData("luckypunch", "Chansey", 1, 2)] // +2 stages for Chansey
    [InlineData("luckypunch", "Mon", 1, 16)]
    [InlineData("stick", "Farfetch’d", 1, 2)] // +2 stages for Farfetch'd
    [InlineData("stick", "Mon", 1, 16)]
    [InlineData(null, "Mon", 1, 16)]
    public void Critical_hit_items_raise_the_crit_stage(string? item, string species, int critRatio, int odds)
    {
        var random = new ScriptedRandom(0, 100);
        var result = Calculator.Calculate(Set(item, species), Set(), Move(critRatio: critRatio), false, random);

        Assert.Equal((0, odds), random.Calls[0]);
        Assert.True(result.Critical);
    }

    [Fact]
    public void A_crit_stage_of_four_always_crits_without_a_roll()
    {
        var random = new ScriptedRandom(100);
        var result = Calculator.Calculate(Set("stick", "Farfetch’d"), Set(), Move(critRatio: 2), false, random);

        Assert.True(result.Critical);
        Assert.Equal([(85, 101)], random.Calls);
    }

    // Showdown's Iron Ball onEffectiveness returns 0 for every type of a Flying holder hit by a
    // Ground move, and runEffectiveness adds the per-type values: the result is always neutral.
    [Theory]
    [InlineData(new[] { PokemonType.Flying }, 24)]
    [InlineData(new[] { PokemonType.Fire, PokemonType.Flying }, 24)] // not x2 from Fire
    [InlineData(new[] { PokemonType.Bug, PokemonType.Flying }, 24)] // not x0.5 from Bug
    [InlineData(new[] { PokemonType.Fire }, 48)] // no Flying type: the chart applies as usual
    public void Iron_ball_makes_ground_moves_neutral_against_flying_holders(PokemonType[] types, int expected)
    {
        var chart = new TestTypeChart(new()
        {
            [(PokemonType.Ground, PokemonType.Flying)] = 0,
            [(PokemonType.Ground, PokemonType.Fire)] = 2,
            [(PokemonType.Ground, PokemonType.Bug)] = 0.5,
        });
        var calculator = new DamageCalculator(chart);
        var defender = Set("ironball") with { Types = types };
        var move = Move(type: PokemonType.Ground);

        Assert.Equal(expected, calculator.Calculate(Set(), defender, move, false, new ScriptedRandom(1, 100)).Damage);
        // Without the item a Flying type is immune again; other types are unaffected by the item.
        var withoutItem = types.Contains(PokemonType.Flying) ? 0 : expected;
        Assert.Equal(withoutItem, calculator.Calculate(Set(), defender with { Item = null }, move, false, new ScriptedRandom(1, 100)).Damage);
    }
}
