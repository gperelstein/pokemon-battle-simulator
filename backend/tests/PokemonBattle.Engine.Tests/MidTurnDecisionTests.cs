using PokemonBattle.Engine.Battles;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

public class MidTurnDecisionTests
{
    [Fact]
    public void Volt_switch_pauses_the_turn_and_asks_only_its_user_for_a_replacement()
    {
        var battle = Singles(
            [Mon("Raichu", speed: 110, moves: [VoltSwitch]), Mon("Jolteon", speed: 130)],
            [Mon("Snorlax", speed: 30)]);
        battle.Start();

        battle.Submit(SideId.P1, Move());
        var result = battle.Submit(SideId.P2, Move());

        Assert.True(result.Accepted);
        Assert.Equal(
            ["P1.0 Raichu uses voltswitch", "P2.0 Snorlax damaged", "request P1 MidTurnSwitch [0]"],
            Describe(result.Events));
        var pending = Assert.Single(battle.PendingRequests);
        Assert.Equal(SideId.P1, pending.Side);
        Assert.Equal(DecisionKind.MidTurnSwitch, pending.Kind);
        Assert.Equal([0], pending.Slots);
    }

    [Fact]
    public void Turn_resumes_with_the_queued_actions_after_the_mid_turn_switch()
    {
        var battle = Singles(
            [Mon("Raichu", speed: 110, moves: [VoltSwitch]), Mon("Jolteon", speed: 130)],
            [Mon("Snorlax", speed: 30)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());
        battle.Submit(SideId.P2, Move());

        var result = battle.Submit(SideId.P1, Switch(slot: 0, teamIndex: 1));

        Assert.True(result.Accepted);
        Assert.Equal(
            [
                "P1.0 out Raichu", "P1.0 in Jolteon",
                "P2.0 Snorlax uses tackle", "P1.0 Jolteon damaged",
                "turn 2", "request P1 Turn [0]", "request P2 Turn [0]",
            ],
            Describe(result.Events));
    }

    [Fact]
    public void Opponent_cannot_submit_while_a_mid_turn_switch_is_pending()
    {
        var battle = Singles(
            [Mon("Raichu", speed: 110, moves: [VoltSwitch]), Mon("Jolteon", speed: 130)],
            [Mon("Snorlax", speed: 30)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());
        battle.Submit(SideId.P2, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.False(result.Accepted);
        Assert.Equal("P2 has no pending decision.", result.Error);
    }

    [Fact]
    public void Volt_switch_without_a_healthy_bench_does_not_ask_for_a_replacement()
    {
        var battle = Singles(
            [Mon("Raichu", speed: 110, moves: [VoltSwitch]), Mon("Jolteon", speed: 130, hp: 0)],
            [Mon("Snorlax", speed: 30)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.Equal(
            [
                "P1.0 Raichu uses voltswitch", "P2.0 Snorlax damaged",
                "P2.0 Snorlax uses tackle", "P1.0 Raichu damaged",
                "turn 2", "request P1 Turn [0]", "request P2 Turn [0]",
            ],
            Describe(result.Events));
    }

    [Fact]
    public void Slower_volt_switch_asks_for_the_replacement_after_the_opponent_moved()
    {
        var battle = Singles(
            [Mon("Raichu", speed: 10, moves: [VoltSwitch]), Mon("Jolteon", speed: 130)],
            [Mon("Snorlax", speed: 30)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.Equal(
            [
                "P2.0 Snorlax uses tackle", "P1.0 Raichu damaged",
                "P1.0 Raichu uses voltswitch", "P2.0 Snorlax damaged",
                "request P1 MidTurnSwitch [0]",
            ],
            Describe(result.Events));
    }

    [Fact]
    public void In_doubles_only_the_volt_switch_slot_is_replaced_and_the_rest_of_the_turn_continues()
    {
        var battle = Doubles(
            [Mon("Raichu", speed: 110, moves: [VoltSwitch]), Mon("Clefable", speed: 60), Mon("Jolteon", speed: 130)],
            [Mon("Snorlax", speed: 30), Mon("Golem", speed: 20)]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 1), new MoveChoice(1, 0, 0)));

        var paused = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 1)));

        Assert.Equal(
            ["P1.0 Raichu uses voltswitch", "P2.1 Golem damaged", "request P1 MidTurnSwitch [0]"],
            Describe(paused.Events));

        var resumed = battle.Submit(SideId.P1, Switch(slot: 0, teamIndex: 2));

        Assert.Equal(
            [
                "P1.0 out Raichu", "P1.0 in Jolteon",
                "P1.1 Clefable uses tackle", "P2.0 Snorlax damaged",
                "P2.0 Snorlax uses tackle", "P1.0 Jolteon damaged",
                "P2.1 Golem uses tackle", "P1.1 Clefable damaged",
                "turn 2", "request P1 Turn [0,1]", "request P2 Turn [0,1]",
            ],
            Describe(resumed.Events));
    }

    [Fact]
    public void Mid_turn_switch_rejects_a_fainted_or_already_active_pokemon()
    {
        var battle = Doubles(
            [Mon("Raichu", speed: 110, moves: [VoltSwitch]), Mon("Clefable", speed: 60), Mon("Jolteon", speed: 130)],
            [Mon("Snorlax", speed: 30), Mon("Golem", speed: 20)]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0)));
        battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0)));

        var result = battle.Submit(SideId.P1, Switch(slot: 0, teamIndex: 1));

        Assert.False(result.Accepted);
        Assert.Equal("Team member 1 cannot be switched in.", result.Error);
        Assert.Equal(DecisionKind.MidTurnSwitch, Assert.Single(battle.PendingRequests).Kind);
    }
}
