using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Engine.Tests;

/// <summary>Showdown's chainModify/finalModify (sim/battle.ts): 4096-based, rounded per chain step.</summary>
public class ModifierChainTests
{
    [Fact]
    public void An_empty_chain_leaves_the_value_unchanged() =>
        Assert.Equal(123, new ModifierChain().Apply(123));

    [Theory]
    [InlineData(100, 3, 2, 150)]
    [InlineData(101, 3, 2, 151)] // 151.5 rounds half down
    [InlineData(101, 1, 2, 50)] // 50.5 rounds half down
    [InlineData(100, 5325, 4096, 130)]
    public void A_single_modifier_rounds_half_down(int value, int numerator, int denominator, int expected) =>
        Assert.Equal(expected, new ModifierChain().Chain(numerator, denominator).Apply(value));

    [Fact]
    public void Chained_modifiers_are_combined_in_4096ths_before_being_applied_once()
    {
        // 6144 x 5325: (32716800 + 2048) >> 12 = 7988, so 100 becomes (798800 + 2047) / 4096 = 195.
        var chain = new ModifierChain().Chain(3, 2).Chain(5325, 4096);

        Assert.Equal(7988, chain.Modifier);
        Assert.Equal(195, chain.Apply(100));
    }

    [Fact]
    public void Opposite_modifiers_can_cancel_out()
    {
        // Guts x1.5 against Thick Fat x0.5: 6144 x 2048 -> 3072, i.e. x0.75.
        var chain = new ModifierChain().Chain(3, 2).Chain(1, 2);

        Assert.Equal(3072, chain.Modifier);
        Assert.Equal(75, chain.Apply(100));
    }
}
