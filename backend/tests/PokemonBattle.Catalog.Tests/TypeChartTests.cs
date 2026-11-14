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

    [Theory]
    [InlineData(PokemonType.Electric, TypeCondition.Paralysis, true)] // Gen 6+ only
    [InlineData(PokemonType.Fire, TypeCondition.Burn, true)]
    [InlineData(PokemonType.Ice, TypeCondition.Freeze, true)]
    [InlineData(PokemonType.Poison, TypeCondition.Poison, true)]
    [InlineData(PokemonType.Steel, TypeCondition.Poison, true)]
    [InlineData(PokemonType.Grass, TypeCondition.Powder, true)] // Gen 6+ only
    [InlineData(PokemonType.Ground, TypeCondition.Sandstorm, true)]
    [InlineData(PokemonType.Ice, TypeCondition.Hail, true)]
    [InlineData(PokemonType.Ghost, TypeCondition.Trapped, true)]
    [InlineData(PokemonType.Normal, TypeCondition.Paralysis, false)]
    [InlineData(PokemonType.Ground, TypeCondition.Paralysis, false)]
    [InlineData(PokemonType.Water, TypeCondition.Freeze, false)]
    public void Uses_gen_6_condition_immunities(PokemonType type, TypeCondition condition, bool immune)
    {
        Assert.Equal(immune, Chart.IsImmune([type], condition));
        Assert.Equal(immune, Chart.IsImmune([PokemonType.Normal, type], condition));
    }
}
