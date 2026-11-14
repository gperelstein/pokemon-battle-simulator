using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Catalog.Tests;

public class TeamBuilderTests
{
    private static readonly TeamBuilder Builder = new(Gen6Catalog.Instance);

    private static PokemonSetSpec Mewtwo() => new(
        Species: "Mewtwo",
        Moves: ["Psystrike", "Aura Sphere", "Ice Beam", "Recover"],
        Ability: "Unnerve",
        Item: "Mewtwonite Y",
        Nature: "Timid",
        Evs: new StatSpread(4, 0, 0, 252, 0, 252));

    private static IReadOnlyList<string> ErrorsFor(PokemonSetSpec spec) => Builder.Build([spec]).Errors;

    [Fact]
    public void Builds_an_engine_set_with_gen_6_stats_and_moves()
    {
        var result = Builder.Build([Mewtwo(), new PokemonSetSpec("Raichu", ["Volt Switch", "Surf"], "Static")]);

        Assert.Empty(result.Errors);
        var mewtwo = result.Team[0];
        Assert.Equal("Mewtwo", mewtwo.Species);
        Assert.Equal([PokemonType.Psychic], mewtwo.Types);
        Assert.Equal(new Stats(354, 230, 216, 407, 216, 394), mewtwo.Stats);
        Assert.Equal("unnerve", mewtwo.Ability);
        Assert.Equal("mewtwonitey", mewtwo.Item);
        Assert.Equal(["psystrike", "aurasphere", "icebeam", "recover"], mewtwo.Moves.Select(m => m.Id));

        var raichuMoves = result.Team[1].Moves;
        Assert.True(raichuMoves[0].SelfSwitch);
        Assert.Equal(MoveTarget.AllAdjacent, raichuMoves[1].Target);
        Assert.Equal(MoveCategory.Special, raichuMoves[1].Category);
    }

    [Fact]
    public void Defaults_to_level_100_with_perfect_ivs_and_no_evs()
    {
        var result = Builder.Build([new PokemonSetSpec("Mewtwo", ["Psystrike"], "Pressure")]);

        // HP: 2*106+31 = 243 -> 243+100+10.
        Assert.Equal(353, result.Team[0].Stats.Hp);
        Assert.Equal(100, result.Team[0].Level);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Rejects_teams_outside_one_to_six_pokemon(int size)
    {
        var result = Builder.Build(Enumerable.Repeat(Mewtwo(), size).ToArray());

        Assert.Equal(["A team must have between 1 and 6 Pokémon."], result.Errors);
        Assert.Empty(result.Team);
    }

    [Fact]
    public void Rejects_species_outside_the_available_catalog()
    {
        Assert.Equal(["Slot 1: species 'Chikorita' is not available."], ErrorsFor(Mewtwo() with { Species = "Chikorita" }));
    }

    [Fact]
    public void Rejects_mega_evolutions_as_team_members()
    {
        Assert.Equal(
            ["Slot 1: Mewtwo-Mega-Y can only appear by Mega Evolving in battle."],
            ErrorsFor(Mewtwo() with { Species = "Mewtwo-Mega-Y", Ability = "Insomnia" }));
    }

    [Fact]
    public void Rejects_illegal_unknown_and_repeated_moves()
    {
        var errors = ErrorsFor(Mewtwo() with { Moves = ["Spore", "Moonblast Z", "Recover", "Recover"] });

        Assert.Equal(
            [
                "Slot 1: Mewtwo cannot learn Spore.",
                "Slot 1: move 'Moonblast Z' does not exist in Gen 6.",
                "Slot 1: Recover is repeated.",
            ],
            errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Rejects_move_counts_outside_one_to_four(int count)
    {
        var moves = new[] { "Psystrike", "Aura Sphere", "Ice Beam", "Recover", "Calm Mind" }.Take(count).ToArray();

        Assert.Equal(["Slot 1: must know between 1 and 4 moves."], ErrorsFor(Mewtwo() with { Moves = moves }));
    }

    [Fact]
    public void Rejects_an_ability_the_species_cannot_have()
    {
        Assert.Equal(
            ["Slot 1: Mewtwo cannot have the ability 'Drought' (Pressure, Unnerve)."],
            ErrorsFor(Mewtwo() with { Ability = "Drought" }));
    }

    [Fact]
    public void Rejects_unknown_items_and_natures()
    {
        Assert.Equal(
            ["Slot 1: item 'Z-Crystal' does not exist in Gen 6.", "Slot 1: nature 'Grumpy' does not exist."],
            ErrorsFor(Mewtwo() with { Item = "Z-Crystal", Nature = "Grumpy" }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Rejects_levels_outside_one_to_one_hundred(int level)
    {
        Assert.Equal(["Slot 1: level must be between 1 and 100."], ErrorsFor(Mewtwo() with { Level = level }));
    }

    [Fact]
    public void Rejects_more_than_252_evs_in_a_stat_or_510_in_total()
    {
        const string error = "Slot 1: EVs must be between 0 and 252 per stat and 510 in total.";
        Assert.Equal([error], ErrorsFor(Mewtwo() with { Evs = new StatSpread(0, 0, 0, 253, 0, 0) }));
        Assert.Equal([error], ErrorsFor(Mewtwo() with { Evs = new StatSpread(8, 0, 0, 252, 0, 252) }));
    }

    [Fact]
    public void Rejects_ivs_outside_zero_to_thirty_one()
    {
        Assert.Equal(
            ["Slot 1: IVs must be between 0 and 31."],
            ErrorsFor(Mewtwo() with { Ivs = new StatSpread(31, 31, 31, 32, 31, 31) }));
    }

    [Fact]
    public void Marks_abilities_that_mold_breaker_can_ignore()
    {
        var team = Builder.Build(
        [
            new PokemonSetSpec("Gengar", ["Shadow Ball"], "Levitate"),
            new PokemonSetSpec("Clefable", ["Moonblast"], "Magic Guard"),
            new PokemonSetSpec("Pinsir", ["X-Scissor"], "Mold Breaker"),
        ]).Team;

        Assert.Equal([true, false, false], team.Select(set => set.AbilityBreakable));
    }

    [Fact]
    public void Marks_abilities_that_trace_cannot_copy()
    {
        var team = Builder.Build(
        [
            new PokemonSetSpec("Porygon", ["Tackle"], "Trace"),
            new PokemonSetSpec("Ditto", ["Transform"], "Imposter"),
            new PokemonSetSpec("Gyarados", ["Waterfall"], "Intimidate"),
        ]).Team;

        Assert.Equal([false, false, true], team.Select(set => set.AbilityTraceable));
    }

    [Fact]
    public void Builds_weight_in_hectograms_and_happiness_defaulting_to_255()
    {
        var team = Builder.Build(
        [
            new PokemonSetSpec("Snorlax", ["Body Slam"], "Thick Fat"),
            new PokemonSetSpec("Charizard", ["Flare Blitz"], "Blaze", Happiness: 0),
        ]).Team;

        Assert.Equal(4600, team[0].WeightHg);
        Assert.Equal(255, team[0].Happiness);
        Assert.Equal(905, team[1].WeightHg);
        Assert.Equal(0, team[1].Happiness);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void Rejects_happiness_outside_zero_to_255(int happiness)
    {
        Assert.Equal(
            ["Slot 1: happiness must be between 0 and 255."],
            ErrorsFor(Mewtwo() with { Happiness = happiness }));
    }

    [Fact]
    public void Marks_species_that_can_still_evolve_for_eviolite()
    {
        var team = Builder.Build(
        [
            new PokemonSetSpec("Charmander", ["Ember"], "Blaze", Item: "Eviolite"),
            new PokemonSetSpec("Charizard", ["Flare Blitz"], "Blaze", Item: "Eviolite"),
        ]).Team;

        Assert.Equal([true, false], team.Select(set => set.CanEvolve));
    }

    [Fact]
    public void Records_the_base_species_of_formes()
    {
        var team = Builder.Build(
        [
            new PokemonSetSpec("Pikachu-Cosplay", ["Thunder Shock"], "Lightning Rod", Item: "Light Ball"),
            new PokemonSetSpec("Pikachu", ["Thunder Shock"], "Static"),
        ]).Team;

        Assert.Equal("Pikachu-Cosplay", team[0].Species);
        Assert.Equal("Pikachu", team[0].BaseSpecies);
        Assert.Equal("Pikachu", team[1].BaseSpecies);
    }

    [Theory]
    [InlineData("Flame Plate", PokemonType.Fire)]
    [InlineData("Pixie Plate", PokemonType.Fairy)]
    [InlineData("Charcoal", null)]
    public void Records_the_type_of_a_held_plate(string item, PokemonType? plateType)
    {
        var team = Builder.Build([new PokemonSetSpec("Charizard", ["Flare Blitz"], "Blaze", Item: item)]).Team;

        Assert.Equal(plateType, team[0].PlateType);
    }
}
