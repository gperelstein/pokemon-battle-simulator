using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Catalog.Tests;

public class Gen6CatalogTests
{
    private static readonly Gen6Catalog Catalog = Gen6Catalog.Instance;

    [Fact]
    public void Contains_generation_1_species_with_their_gen_6_formes_and_megas()
    {
        Assert.Equal(172, Catalog.Species.Count);
        Assert.Equal(151, Catalog.Species.Values.Select(s => s.Num).Distinct().Count());
        Assert.Equal(15, Catalog.Species.Values.Count(s => s.IsMega));
        Assert.Null(Catalog.FindSpecies("Chikorita"));
    }

    [Fact]
    public void Uses_gen_6_base_stats_and_types()
    {
        Assert.Equal(90, Catalog.FindSpecies("Butterfree")!.BaseStats.Spa);
        Assert.Equal([PokemonType.Fairy], Catalog.FindSpecies("Clefable")!.Types);
    }

    [Fact]
    public void Mega_evolution_is_linked_to_its_stone()
    {
        var mega = Catalog.FindSpecies("Charizard-Mega-Y")!;
        var stone = Catalog.FindItem("Charizardite Y")!;

        Assert.Equal("Charizard", mega.BaseSpecies);
        Assert.Equal("Drought", mega.Abilities["0"]);
        Assert.Equal("Charizard-Mega-Y", stone.MegaStone!["Charizard"]);
    }

    [Fact]
    public void Contains_every_gen_6_move_ability_item_and_nature()
    {
        Assert.Equal(634, Catalog.Moves.Count);
        Assert.Equal(191, Catalog.Abilities.Count);
        Assert.Equal(284, Catalog.Items.Count);
        Assert.Equal(25, Catalog.Natures.Count);
        Assert.Equal(80, Catalog.FindMove("Sucker Punch")!.BasePower);
        Assert.Equal(PokemonType.Fire, Catalog.FindMove("Hidden Power Fire")!.Type);
    }

    [Fact]
    public void Exposes_self_switch_kinds()
    {
        Assert.Equal(SelfSwitchKind.Switch, Catalog.FindMove("Volt Switch")!.SelfSwitchKind);
        Assert.Equal(SelfSwitchKind.CopyVolatile, Catalog.FindMove("Baton Pass")!.SelfSwitchKind);
        Assert.Equal(SelfSwitchKind.None, Catalog.FindMove("Thunderbolt")!.SelfSwitchKind);
    }

    [Fact]
    public void Moves_that_never_miss_have_no_accuracy()
    {
        Assert.Null(Catalog.FindMove("Aerial Ace")!.Accuracy);
        Assert.Equal(100, Catalog.FindMove("Thunderbolt")!.Accuracy);
    }

    [Theory]
    [InlineData("Charizard", "Dragon Dance", true)]
    [InlineData("Charizard-Mega-Y", "Solar Beam", true)]
    [InlineData("Charizard", "Spore", false)]
    [InlineData("Charizard", "Hidden Power Ice", true)]
    [InlineData("Charizard", "Not A Move", false)]
    [InlineData("Chikorita", "Tackle", false)]
    public void Knows_which_moves_a_species_can_learn_in_gen_6(string species, string move, bool expected)
    {
        Assert.Equal(expected, Catalog.CanLearn(species, move));
    }

    [Theory]
    [InlineData("Charizard-Mega-Y", "charizardmegay")]
    [InlineData("Mr. Mime", "mrmime")]
    [InlineData("Farfetch’d", "farfetchd")]
    [InlineData("U-turn", "uturn")]
    public void Normalizes_names_to_ids(string name, string id)
    {
        Assert.Equal(id, CatalogId.From(name));
    }
}
