using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

public class BattleDamageTests
{
    private static readonly MoveDefinition Thunderbolt =
        new("thunderbolt", "Thunderbolt", PokemonType.Electric, MoveCategory.Special, 90);

    private static readonly MoveDefinition StormThrow =
        new("stormthrow", "Storm Throw", PokemonType.Fighting, MoveCategory.Physical, 60, WillCrit: true);

    // Trump Card depends on the PP left, which the engine does not track yet, so it stays unsupported.
    private static readonly MoveDefinition TrumpCard =
        new("trumpcard", "Trump Card", PokemonType.Normal, MoveCategory.Special, 0);

    private static PokemonSet Typed(PokemonSet set, params PokemonType[] types) => set with { Types = types };

    private static IReadOnlyList<string> PlayTurn(PokemonSet attacker, PokemonSet defender)
    {
        var battle = Singles([attacker], [defender]);
        battle.Start();
        battle.Submit(SideId.P1, Move());
        return Describe(battle.Submit(SideId.P2, Move()).Events);
    }

    [Fact]
    public void Reports_effectiveness_before_the_damage()
    {
        var events = PlayTurn(
            Typed(Mon("Raichu", 110, moves: [Thunderbolt]), PokemonType.Electric),
            Typed(Mon("Gyarados", 81), PokemonType.Water, PokemonType.Flying));

        Assert.Equal(
            ["P1.0 Raichu uses thunderbolt", "P2.0 effectiveness x2", "P2.0 Gyarados damaged"],
            events.Take(3));
    }

    [Fact]
    public void Immune_target_takes_no_damage()
    {
        var events = PlayTurn(
            Typed(Mon("Raichu", 110, moves: [Thunderbolt]), PokemonType.Electric),
            Typed(Mon("Golem", 45), PokemonType.Rock, PokemonType.Ground));

        Assert.Equal(
            ["P1.0 Raichu uses thunderbolt", "P2.0 Golem immune", "P2.0 Golem uses tackle"],
            events.Take(3));
    }

    [Fact]
    public void Reports_critical_hits_before_the_damage()
    {
        var events = PlayTurn(Mon("Machamp", 55, moves: [StormThrow]), Mon("Snorlax", 30));

        Assert.Equal(
            ["P1.0 Machamp uses stormthrow", "P2.0 critical hit", "P2.0 Snorlax damaged"],
            events.Take(3));
    }

    [Fact]
    public void Volt_switch_into_an_immune_target_does_not_switch_out()
    {
        var battle = Singles(
            [Mon("Raichu", 110, moves: [VoltSwitch]), Mon("Jolteon", 130)],
            [Typed(Mon("Golem", 45), PokemonType.Rock, PokemonType.Ground)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.Equal(["P1.0 Raichu uses voltswitch", "P2.0 Golem immune", "P2.0 Golem uses tackle"], Describe(result.Events).Take(3));
        Assert.DoesNotContain(battle.PendingRequests, r => r.Kind == DecisionKind.MidTurnSwitch);
    }

    [Fact]
    public void Variable_power_moves_fail_until_their_logic_exists()
    {
        var events = PlayTurn(Mon("Machamp", 55, moves: [TrumpCard]), Mon("Snorlax", 30));

        Assert.Equal(["P1.0 Machamp uses trumpcard", "P1.0 trumpcard failed"], events.Take(2));
    }
}
