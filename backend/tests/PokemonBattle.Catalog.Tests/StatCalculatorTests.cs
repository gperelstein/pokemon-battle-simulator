using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Catalog.Tests;

public class StatCalculatorTests
{
    private static readonly Gen6Catalog Catalog = Gen6Catalog.Instance;

    [Fact]
    public void Matches_the_reference_example_for_the_gen_3_onwards_formula()
    {
        // Bulbapedia's reference example: level 78 Adamant Garchomp.
        var stats = StatCalculator.Calculate(
            new BaseStats(108, 130, 95, 80, 85, 102),
            level: 78,
            ivs: new StatSpread(24, 12, 30, 16, 23, 5),
            evs: new StatSpread(74, 190, 91, 48, 84, 23),
            Catalog.FindNature("Adamant")!);

        Assert.Equal(new Stats(289, 278, 193, 135, 171, 171), stats);
    }

    [Fact]
    public void Calculates_max_speed_timid_mewtwo()
    {
        var stats = StatCalculator.Calculate(
            Catalog.FindSpecies("Mewtwo")!.BaseStats,
            level: 100,
            ivs: StatSpread.All(31),
            evs: new StatSpread(4, 0, 0, 252, 0, 252),
            Catalog.FindNature("Timid")!);

        // HP: 2*106+31+4/4 = 244 -> 244+100+10. Speed: (2*130+31+63)+5 = 359 -> *1.1 = 394.
        // Attack is lowered by Timid: (2*110+31)+5 = 256 -> *0.9 = 230.
        Assert.Equal(new Stats(354, 230, 216, 407, 216, 394), stats);
    }
}
