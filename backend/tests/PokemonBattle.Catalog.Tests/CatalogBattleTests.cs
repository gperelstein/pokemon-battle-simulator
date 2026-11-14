using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Catalog.Tests;

/// <summary>End-to-end: catalog-built teams fighting with the real Gen 6 type chart.</summary>
public class CatalogBattleTests
{
    private static readonly Gen6Catalog Catalog = Gen6Catalog.Instance;
    private static readonly TeamBuilder Builder = new(Catalog);

    private static IReadOnlyList<BattleEvent> PlayFirstTurn(PokemonSetSpec p1, PokemonSetSpec p2)
    {
        var p1Team = Builder.Build([p1]);
        var p2Team = Builder.Build([p2]);
        Assert.Empty(p1Team.Errors.Concat(p2Team.Errors));

        var battle = Battle.Create(BattleFormat.Singles, p1Team.Team, p2Team.Team, Catalog.TypeChart, new SeededBattleRandom(7));
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0)));
        return battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0))).Events;
    }

    [Fact]
    public void Electric_move_is_four_times_effective_against_gyarados()
    {
        var events = PlayFirstTurn(
            new PokemonSetSpec("Raichu", ["Thunderbolt"], "Static"),
            new PokemonSetSpec("Gyarados", ["Waterfall"], "Intimidate"));

        Assert.Contains(new Effectiveness(SideId.P2, 0, 4), events);
    }

    [Fact]
    public void Seismic_toss_deals_the_users_level_and_cannot_touch_ghosts()
    {
        var vsGyarados = PlayFirstTurn(
            new PokemonSetSpec("Machamp", ["Seismic Toss"], "Guts"),
            new PokemonSetSpec("Gyarados", ["Waterfall"], "Intimidate"));
        var vsGengar = PlayFirstTurn(
            new PokemonSetSpec("Machamp", ["Seismic Toss"], "Guts"),
            new PokemonSetSpec("Gengar", ["Shadow Ball"], "Levitate"));

        Assert.Contains(vsGyarados, e => e is DamageDealt { Side: SideId.P2, Amount: 100 });
        Assert.Contains(new Immune(SideId.P2, 0, "Gengar"), vsGengar);
    }

    [Fact]
    public void Moves_map_accuracy_and_stat_changes_from_gen_6_data()
    {
        MoveDefinition Move(string id) => Catalog.FindMove(id)!.ToDefinition();

        var swordsDance = Move("swordsdance");
        Assert.Null(swordsDance.Accuracy);
        Assert.Equal(MoveTarget.Self, swordsDance.Target);
        Assert.Equal([new StatChange(BoostStat.Attack, 2)], swordsDance.Boosts);

        Assert.Equal(70, Move("thunder").Accuracy);
        Assert.Equal([new StatChange(BoostStat.Accuracy, -1)], Move("sandattack").Boosts);
        Assert.Equal([new StatChange(BoostStat.Evasion, 1)], Move("doubleteam").Boosts);

        var closeCombat = Move("closecombat");
        Assert.Equal([new StatChange(BoostStat.Defense, -1), new StatChange(BoostStat.SpecialDefense, -1)], closeCombat.SelfBoosts);
        Assert.Null(closeCombat.SelfBoostChance);

        var diamondStorm = Move("diamondstorm");
        Assert.Equal([new StatChange(BoostStat.Defense, 2)], diamondStorm.SelfBoosts);
        Assert.Equal(50, diamondStorm.SelfBoostChance);

        var psychic = Assert.Single(Move("psychic").Secondaries);
        Assert.Equal(10, psychic.Chance);
        Assert.Equal([new StatChange(BoostStat.SpecialDefense, -1)], psychic.TargetBoosts);

        var flameCharge = Assert.Single(Move("flamecharge").Secondaries);
        Assert.Equal(100, flameCharge.Chance);
        Assert.Equal([new StatChange(BoostStat.Speed, 1)], flameCharge.SelfBoosts);

        var chipAway = Move("chipaway");
        Assert.True(chipAway.IgnoreEvasion);
        Assert.True(chipAway.IgnoreDefensive);
        Assert.False(Move("tackle").IgnoreEvasion);
    }

    [Fact]
    public void Swords_dance_from_the_catalog_raises_attack()
    {
        var events = PlayFirstTurn(
            new PokemonSetSpec("Scyther", ["Swords Dance"], "Technician"),
            new PokemonSetSpec("Snorlax", ["Body Slam"], "Thick Fat"));

        Assert.Contains(new StatStageChanged(SideId.P1, 0, "Scyther", BoostStat.Attack, 2, 2), events);
    }

    [Fact]
    public void Builder_maps_crit_stage_and_fixed_damage()
    {
        var team = Builder.Build([new PokemonSetSpec("Charizard", ["Slash", "Dragon Rage"], "Blaze")]).Team;

        Assert.Equal(2, team[0].Moves[0].CritRatio);
        Assert.Equal(40, team[0].Moves[1].FixedDamage);
    }
}
