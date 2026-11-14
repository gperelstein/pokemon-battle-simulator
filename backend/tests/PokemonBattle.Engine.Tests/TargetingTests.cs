using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

public class TargetingTests
{
    private static readonly MoveDefinition Earthquake =
        new("earthquake", "Earthquake", PokemonType.Ground, MoveCategory.Physical, 100, Target: MoveTarget.AllAdjacent);

    private static readonly MoveDefinition SwordsDance =
        new("swordsdance", "Swords Dance", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition Outrage =
        new("outrage", "Outrage", PokemonType.Dragon, MoveCategory.Physical, 120, Target: MoveTarget.RandomNormal);

    private static readonly MoveDefinition BatonPass =
        new("batonpass", "Baton Pass", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self, SelfSwitch: true);

    [Fact]
    public void All_adjacent_move_also_hits_the_ally_in_doubles()
    {
        var battle = Doubles(
            [Mon("Golem", 45, moves: [Earthquake]), Mon("Pidgeot", 10)],
            [Mon("Onix", 30), Mon("Abra", 20)]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0, 0)));

        var result = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0)));

        Assert.Equal(
            ["P1.0 Golem uses earthquake", "P2.0 Onix damaged", "P2.1 Abra damaged", "P1.1 Pidgeot damaged"],
            Describe(result.Events).Take(4));
    }

    [Fact]
    public void All_adjacent_move_only_hits_the_foe_in_singles()
    {
        var battle = Singles([Mon("Golem", 45, moves: [Earthquake])], [Mon("Onix", 30)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.Equal(["P1.0 Golem uses earthquake", "P2.0 Onix damaged"], Describe(result.Events).Take(2));
    }

    [Fact]
    public void Self_targeting_status_move_neither_fails_nor_deals_damage()
    {
        var battle = Singles([Mon("Scyther", 105, moves: [SwordsDance])], [Mon("Onix", 30)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.Equal(["P1.0 Scyther uses swordsdance", "P2.0 Onix uses tackle"], Describe(result.Events).Take(2));
    }

    [Fact]
    public void Self_targeting_self_switch_move_still_asks_for_a_replacement()
    {
        var battle = Singles([Mon("Jolteon", 130, moves: [BatonPass]), Mon("Onix", 30)], [Mon("Abra", 90)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.Equal(["P1.0 Jolteon uses batonpass", "request P1 MidTurnSwitch [0]"], Describe(result.Events));
    }

    [Fact]
    public void Random_target_move_hits_a_live_foe()
    {
        var battle = Doubles(
            [Mon("Dragonite", 80, moves: [Outrage]), Mon("Pidgeot", 10)],
            [Mon("Onix", 30), Mon("Abra", 20)]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0, 0)));

        var result = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0)));

        // MaxRandom picks the last live foe.
        Assert.Equal(["P1.0 Dragonite uses outrage", "P2.1 Abra damaged"], Describe(result.Events).Take(2));
    }
}
