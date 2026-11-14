using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

public class AccuracyTests
{
    private static readonly MoveDefinition MegaKick =
        new("megakick", "Mega Kick", PokemonType.Normal, MoveCategory.Physical, 120, Accuracy: 75);

    private static readonly MoveDefinition AccurateTackle = Tackle with { Accuracy = 100 };

    private static readonly MoveDefinition SandAttack = new(
        "sandattack", "Sand-Attack", PokemonType.Ground, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Accuracy: 100, Boosts: [Stage(BoostStat.Accuracy, -1)]);

    private static readonly MoveDefinition DoubleTeam = new(
        "doubleteam", "Double Team", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self,
        Boosts: [Stage(BoostStat.Evasion, 1)]);

    private static readonly MoveDefinition ChipAway = new(
        "chipaway", "Chip Away", PokemonType.Normal, MoveCategory.Physical, 70, Accuracy: 100,
        IgnoreEvasion: true, IgnoreDefensive: true);

    private static readonly MoveDefinition Swift = new(
        "swift", "Swift", PokemonType.Normal, MoveCategory.Special, 60, Target: MoveTarget.AllAdjacentFoes);

    private static IReadOnlyList<string> PlayTurn(Battle battle)
    {
        battle.Submit(SideId.P1, Move());
        return Describe(battle.Submit(SideId.P2, Move()).Events);
    }

    [Theory]
    [InlineData(74, true)]
    [InlineData(75, false)]
    public void Moves_hit_when_the_roll_is_below_their_accuracy(int roll, bool hits)
    {
        var battle = Singles([Mon("Hitmonlee", 87, moves: [MegaKick])], [Mon("Snorlax", 30)], new PercentRolls(roll));
        battle.Start();

        var events = PlayTurn(battle);

        Assert.Equal("P1.0 Hitmonlee uses megakick", events[0]);
        Assert.Equal(hits ? "P2.0 Snorlax damaged" : "P2.0 Snorlax avoided", events[1]);
        Assert.Equal("P2.0 Snorlax uses tackle", events[2]);
    }

    [Fact]
    public void Lowered_accuracy_makes_a_perfectly_accurate_move_miss()
    {
        // Accuracy -1: 100 * 3 / 4 = 75, so a roll of 75 misses.
        var rolls = new PercentRolls(75);
        var battle = Singles([Mon("Sandshrew", 40, moves: [SandAttack])], [Mon("Rattata", 20, moves: [AccurateTackle])], rolls);
        battle.Start();

        var events = PlayTurn(battle);

        Assert.Equal(
            ["P1.0 Sandshrew uses sandattack", "P2.0 Rattata Accuracy -1 = -1", "P2.0 Rattata uses tackle", "P1.0 Sandshrew avoided"],
            events.Take(4));
        Assert.Equal(0, rolls.Remaining);
    }

    [Fact]
    public void Raised_evasion_makes_a_perfectly_accurate_move_roll()
    {
        var rolls = new PercentRolls(75);
        var battle = Singles([Mon("Rattata", 20, moves: [AccurateTackle])], [Mon("Ditto", 48, moves: [DoubleTeam])], rolls);
        battle.Start();

        var events = PlayTurn(battle);

        Assert.Contains("P2.0 Ditto avoided", events);
        Assert.Equal(0, rolls.Remaining);
    }

    [Theory]
    [InlineData("chipaway")]
    [InlineData("swift")]
    public void Some_moves_ignore_evasion(string moveId)
    {
        var move = moveId == "swift" ? Swift : ChipAway;
        // No percentage roll is scripted: the move must hit without rolling.
        var battle = Singles([Mon("Rattata", 20, moves: [move])], [Mon("Ditto", 48, moves: [DoubleTeam])], new PercentRolls());
        battle.Start();

        var events = PlayTurn(battle);

        Assert.Contains("P2.0 Ditto damaged", events);
    }

    [Fact]
    public void A_volt_switch_that_misses_does_not_switch_out()
    {
        var voltSwitch = VoltSwitch with { Accuracy = 100 };
        var battle = Singles(
            [Mon("Raichu", 20, moves: [voltSwitch]), Mon("Jolteon", 130)],
            [Mon("Ditto", 48, moves: [DoubleTeam])],
            new PercentRolls(99));
        battle.Start();

        var events = PlayTurn(battle);

        Assert.Contains("P2.0 Ditto avoided", events);
        Assert.DoesNotContain(battle.PendingRequests, r => r.Kind == DecisionKind.MidTurnSwitch);
    }
}
