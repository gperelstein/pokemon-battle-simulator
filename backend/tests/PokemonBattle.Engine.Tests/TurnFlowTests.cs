using PokemonBattle.Engine.Battles;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

public class TurnFlowTests
{
    [Fact]
    public void Start_sends_in_leads_and_requests_turn_choices_for_every_active_slot()
    {
        var battle = Doubles(
            [Mon("Pikachu", 90), Mon("Mew", 100)],
            [Mon("Onix", 70), Mon("Abra", 90)]);

        var events = battle.Start();

        Assert.Equal(
            [
                "P1.0 in Pikachu", "P1.1 in Mew", "P2.0 in Onix", "P2.1 in Abra",
                "turn 1", "request P1 Turn [0,1]", "request P2 Turn [0,1]",
            ],
            Describe(events));
    }

    [Fact]
    public void Turn_does_not_resolve_until_both_players_submit()
    {
        var battle = Singles([Mon("Pikachu", 90)], [Mon("Onix", 70)]);
        battle.Start();

        var result = battle.Submit(SideId.P1, Move());

        Assert.True(result.Accepted);
        Assert.Empty(result.Events);
        Assert.Equal(SideId.P2, Assert.Single(battle.PendingRequests).Side);
    }

    [Fact]
    public void Priority_beats_speed()
    {
        var battle = Singles([Mon("Rattata", 20, moves: [QuickAttack])], [Mon("Jolteon", 130)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.Equal("P1.0 Rattata uses quickattack", Describe(result.Events)[0]);
    }

    [Fact]
    public void Switching_happens_before_any_move()
    {
        var battle = Singles([Mon("Pikachu", 10), Mon("Onix", 5)], [Mon("Jolteon", 130)]);
        battle.Start();
        battle.Submit(SideId.P1, Switch(slot: 0, teamIndex: 1));

        var result = battle.Submit(SideId.P2, Move());

        Assert.Equal(
            ["P1.0 out Pikachu", "P1.0 in Onix", "P2.0 Jolteon uses tackle", "P1.0 Onix damaged"],
            Describe(result.Events).Take(4));
    }

    [Fact]
    public void Fainted_pokemon_skips_its_move_and_is_replaced_at_end_of_turn()
    {
        var battle = Singles([Mon("Jolteon", 130)], [Mon("Abra", 90, hp: 1), Mon("Onix", 70)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.Equal(
            [
                "P1.0 Jolteon uses tackle", "P2.0 Abra damaged", "P2.0 Abra fainted",
                "request P2 FaintReplacement [0]",
            ],
            Describe(result.Events));

        var replaced = battle.Submit(SideId.P2, Switch(slot: 0, teamIndex: 1));

        Assert.Equal(
            ["P2.0 in Onix", "turn 2", "request P1 Turn [0]", "request P2 Turn [0]"],
            Describe(replaced.Events));
    }

    [Fact]
    public void Doubles_move_retargets_when_its_target_already_fainted()
    {
        var battle = Doubles(
            [Mon("Jolteon", 130), Mon("Pikachu", 90)],
            [Mon("Abra", 50, hp: 1), Mon("Onix", 10)]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0)));

        var result = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0)));

        Assert.Equal(
            [
                "P1.0 Jolteon uses tackle", "P2.0 Abra damaged", "P2.0 Abra fainted",
                "P1.1 Pikachu uses tackle", "P2.1 Onix damaged",
                "P2.1 Onix uses tackle", "P1.0 Jolteon damaged",
                "turn 2", "request P1 Turn [0,1]", "request P2 Turn [1]",
            ],
            Describe(result.Events));
    }

    [Fact]
    public void Spread_move_hits_every_adjacent_foe()
    {
        var battle = Doubles(
            [Mon("Lapras", 60, moves: [Surf]), Mon("Pikachu", 90)],
            [Mon("Onix", 10), Mon("Abra", 20)]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0, 0)));

        var result = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0)));

        Assert.Equal(
            ["P1.0 Lapras uses surf", "P2.0 Onix damaged", "P2.1 Abra damaged"],
            Describe(result.Events).Skip(2).Take(3));
    }

    [Fact]
    public void Losing_the_last_pokemon_ends_the_battle()
    {
        var battle = Singles([Mon("Jolteon", 130)], [Mon("Abra", 90, hp: 1)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.Equal("winner P1", Describe(result.Events)[^1]);
        Assert.True(battle.IsOver);
        Assert.Equal(SideId.P1, battle.Winner);
        Assert.Empty(battle.PendingRequests);
    }

    [Theory]
    [InlineData(4, "Move 4 does not exist.")]
    [InlineData(-1, "Move -1 does not exist.")]
    public void Rejects_an_unknown_move(int moveIndex, string error)
    {
        var battle = Singles([Mon("Pikachu", 90)], [Mon("Onix", 70)]);
        battle.Start();

        var result = battle.Submit(SideId.P1, Move(moveIndex: moveIndex));

        Assert.False(result.Accepted);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Rejects_a_decision_that_does_not_cover_every_requested_slot()
    {
        var battle = Doubles([Mon("Pikachu", 90), Mon("Mew", 100)], [Mon("Onix", 70), Mon("Abra", 90)]);
        battle.Start();

        var result = battle.Submit(SideId.P1, Move(slot: 0));

        Assert.False(result.Accepted);
        Assert.Equal("Expected exactly one choice for each slot: 0, 1.", result.Error);
    }

    [Fact]
    public void Rejects_two_slots_switching_to_the_same_pokemon()
    {
        var battle = Doubles(
            [Mon("Pikachu", 90), Mon("Mew", 100), Mon("Onix", 70)],
            [Mon("Onix", 70), Mon("Abra", 90)]);
        battle.Start();

        var result = battle.Submit(SideId.P1, new Decision(new SwitchChoice(0, 2), new SwitchChoice(1, 2)));

        Assert.False(result.Accepted);
        Assert.Equal("Team member 2 is chosen more than once.", result.Error);
    }

    [Fact]
    public void Faint_replacement_only_accepts_switches()
    {
        var battle = Singles([Mon("Jolteon", 130)], [Mon("Abra", 90, hp: 1), Mon("Onix", 70)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());
        battle.Submit(SideId.P2, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.False(result.Accepted);
        Assert.Equal("Only switches are allowed for FaintReplacement.", result.Error);
    }
}
