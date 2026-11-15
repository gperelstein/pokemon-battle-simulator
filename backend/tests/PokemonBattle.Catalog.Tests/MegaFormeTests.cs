using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Catalog.Tests;

/// <summary>Mega formes built from the catalog's Gen 6 species and Mega Stone data.</summary>
public class MegaFormeTests
{
    private static readonly Gen6Catalog Catalog = Gen6Catalog.Instance;
    private static readonly TeamBuilder Builder = new(Catalog);

    public static TheoryData<string> MegaSpecies() => new(Catalog.Species.Values.Where(s => s.IsMega).Select(s => s.Name));

    private static readonly IReadOnlyList<PokemonType> Types = [PokemonType.Psychic];

    private static PokemonSet Build(PokemonSetSpec spec)
    {
        var result = Builder.Build([spec]);
        Assert.Empty(result.Errors);
        return result.Team[0];
    }

    [Fact]
    public void A_set_holding_its_mega_stone_carries_the_mega_forme_with_its_stats()
    {
        var mewtwo = Build(new PokemonSetSpec(
            "Mewtwo", ["Psystrike"], "Unnerve", "Mewtwonite Y", "Timid", Evs: new StatSpread(4, 0, 0, 252, 0, 252)));

        Assert.Equal(
            new MegaForme("Mewtwo-Mega-Y", "mewtwonitey", Types, new Stats(354, 302, 176, 487, 276, 416),
                "insomnia", 330, AbilityBreakable: true, AbilityTraceable: true),
            mewtwo.Mega! with { Types = Types });
        Assert.Equal([PokemonType.Psychic], mewtwo.Mega!.Types);
    }

    [Theory]
    [MemberData(nameof(MegaSpecies))]
    public void Every_gen_1_mega_is_reachable_from_its_base_species_with_its_stone(string megaName)
    {
        var mega = Catalog.FindSpecies(megaName)!;
        var baseSpecies = Catalog.FindSpecies(mega.BaseSpecies)!;
        var move = Catalog.Moves.Values.First(m => Catalog.CanLearn(baseSpecies.Id, m.Id));
        var ability = Catalog.FindAbility(mega.Abilities["0"])!;

        var set = Build(new PokemonSetSpec(baseSpecies.Name, [move.Name], baseSpecies.Abilities["0"], mega.RequiredItem));

        Assert.Equal(baseSpecies.BaseStats.Hp, mega.BaseStats.Hp);
        var forme = set.Mega!;
        Assert.Equal(mega.Name, forme.Species);
        Assert.Equal(CatalogId.From(mega.RequiredItem!), forme.Item);
        Assert.Equal(mega.Types, forme.Types);
        Assert.Equal(StatCalculator.Calculate(mega.BaseStats, 100, StatSpread.All(31), StatSpread.All(0), Catalog.FindNature("Serious")!), forme.Stats);
        Assert.Equal(ability.Id, forme.Ability);
        Assert.Equal((int)Math.Round(mega.Weightkg * 10), forme.WeightHg);
        Assert.Equal(ability.Flags.Contains("breakable"), forme.AbilityBreakable);
        Assert.Equal(!ability.Flags.Contains("notrace"), forme.AbilityTraceable);
    }

    [Theory]
    [InlineData("Charizard", null)]
    [InlineData("Charizard", "Leftovers")]
    [InlineData("Venusaur", "Charizardite X")]
    [InlineData("Raichu", "Charizardite Y")]
    public void Sets_without_their_own_mega_stone_have_no_mega_forme(string species, string? item)
    {
        var move = Catalog.Moves.Values.First(m => Catalog.CanLearn(CatalogId.From(species), m.Id));
        var ability = Catalog.FindSpecies(species)!.Abilities["0"];

        Assert.Null(Build(new PokemonSetSpec(species, [move.Name], ability, item)).Mega);
    }

    [Fact]
    public void Charizard_mega_evolves_into_its_y_forme_and_starts_sun_in_a_catalog_battle()
    {
        var p1 = Builder.Build([new PokemonSetSpec("Charizard", ["Flamethrower"], "Blaze", "Charizardite Y")]);
        var p2 = Builder.Build([new PokemonSetSpec("Blastoise", ["Surf"], "Torrent")]);
        var battle = Battle.Create(BattleFormat.Singles, p1.Team, p2.Team, Catalog.TypeChart, new SeededBattleRandom(7));
        battle.Start();
        Assert.Equal([0], battle.PendingRequests.Single(r => r.Side == SideId.P1).MegaSlots);

        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, MegaEvolve: true)));
        var events = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0))).Events;

        Assert.Equal(
            [new MegaEvolved(SideId.P1, 0, "Charizard", "Charizard-Mega-Y"),
             new AbilityActivated(SideId.P1, 0, "Charizard-Mega-Y", "drought"), new WeatherStarted(Weather.Sun)],
            events.Take(3));
        var charizard = battle.GetSide(SideId.P1).Active[0]!;
        Assert.Equal([PokemonType.Fire, PokemonType.Flying], charizard.Set.Types);
        Assert.Equal(1005, charizard.WeightHg);
        Assert.Equal("charizarditey", charizard.Item);
    }
}
