using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Catalog.Tests;

public class TypeChartTests
{
    private static readonly TypeChart Chart = Gen6Catalog.Instance.TypeChart;

    [Theory]
    [InlineData(PokemonType.Dark, new[] { PokemonType.Steel }, 1)]
    [InlineData(PokemonType.Ghost, new[] { PokemonType.Steel }, 1)]
    [InlineData(PokemonType.Fairy, new[] { PokemonType.Dragon }, 2)]
    [InlineData(PokemonType.Dragon, new[] { PokemonType.Fairy }, 0)]
    [InlineData(PokemonType.Electric, new[] { PokemonType.Ground }, 0)]
    [InlineData(PokemonType.Rock, new[] { PokemonType.Fire, PokemonType.Flying }, 4)]
    [InlineData(PokemonType.Ground, new[] { PokemonType.Fire, PokemonType.Flying }, 0)]
    [InlineData(PokemonType.Fire, new[] { PokemonType.Water, PokemonType.Dragon }, 0.25)]
    public void Uses_gen_6_effectiveness(PokemonType attacking, PokemonType[] defending, double expected)
    {
        Assert.Equal(expected, Chart.Effectiveness(attacking, defending));
    }
}
