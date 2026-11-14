using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Charge, semi-invulnerable and recharge moves, checked against Showdown's twoturnmove and
/// mustrecharge conditions. A slot locked into its next action gets no decision request (S5).
/// </summary>
public class TwoTurnMoveTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static MoveDefinition Charge(string id, PokemonType type, int power, MoveCategory category = MoveCategory.Physical) =>
        new(id, id, type, category, power, Accuracy: 100, Charge: true);

    private static readonly MoveDefinition SolarBeam = Charge("solarbeam", PokemonType.Grass, 120, MoveCategory.Special);
    private static readonly MoveDefinition SkullBash = Charge("skullbash", PokemonType.Normal, 130);
    private static readonly MoveDefinition Fly = Charge("fly", PokemonType.Flying, 90);

    private static readonly MoveDefinition HyperBeam = new(
        "hyperbeam", "Hyper Beam", PokemonType.Normal, MoveCategory.Special, 150, Accuracy: 90, Recharge: true);

    private static readonly MoveDefinition SunnyDay = new(
        "sunnyday", "Sunny Day", PokemonType.Fire, MoveCategory.Status, 0, Target: MoveTarget.All, SetsWeather: Weather.Sun);

    private static readonly MoveDefinition SandstormMove = new(
        "sandstorm", "Sandstorm", PokemonType.Rock, MoveCategory.Status, 0, Target: MoveTarget.All,
        SetsWeather: Weather.Sandstorm);

    private static readonly MoveDefinition Paralyze = new(
        "inflict", "Inflict", PokemonType.Normal, MoveCategory.Status, 0, Priority: 1, Target: MoveTarget.Normal,
        Status: MajorStatus.Paralysis);

    private static readonly Dictionary<string, MoveDefinition> ChargeMoves = new()
    {
        ["fly"] = Fly,
        ["bounce"] = Charge("bounce", PokemonType.Flying, 85),
        ["dig"] = Charge("dig", PokemonType.Ground, 80),
        ["dive"] = Charge("dive", PokemonType.Water, 80),
        ["shadowforce"] = Charge("shadowforce", PokemonType.Ghost, 120),
        ["phantomforce"] = Charge("phantomforce", PokemonType.Ghost, 90),
    };

    private static readonly Dictionary<string, MoveDefinition> FoeMoves = new()
    {
        ["tackle"] = Tackle,
        ["gust"] = new("gust", "Gust", PokemonType.Flying, MoveCategory.Special, 40),
        ["thunder"] = new("thunder", "Thunder", PokemonType.Electric, MoveCategory.Special, 110, Accuracy: 70),
        ["skyuppercut"] = new("skyuppercut", "Sky Uppercut", PokemonType.Fighting, MoveCategory.Physical, 85),
        ["earthquake"] = new("earthquake", "Earthquake", PokemonType.Ground, MoveCategory.Physical, 100,
            Target: MoveTarget.AllAdjacent),
        ["surf"] = new("surf", "Surf", PokemonType.Water, MoveCategory.Special, 90, Target: MoveTarget.AllAdjacentFoes),
    };

    private static IReadOnlyList<string> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return Describe(battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events);
    }

    private static IReadOnlyList<string> OnlyP2(Battle battle, int p2Move = 0) =>
        Describe(battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events);

    [Fact]
    public void Charge_moves_charge_then_hit_next_turn_without_asking_the_player()
    {
        var battle = Singles([Mon("Venusaur", 100, moves: [SolarBeam])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var charging = Turn(battle);

        Assert.Equal(
            ["P1.0 Venusaur uses solarbeam", "P1.0 Venusaur charges solarbeam", "P2.0 Foe uses splash"],
            charging.Take(3));
        var request = Assert.Single(battle.PendingRequests);
        Assert.Equal((SideId.P2, DecisionKind.Turn), (request.Side, request.Kind));
        Assert.Equal([0], request.Slots);
        Assert.False(battle.Submit(SideId.P1, Move()).Accepted);

        var attack = OnlyP2(battle);

        Assert.Equal(["P1.0 Venusaur uses solarbeam", "P2.0 Foe damaged"], attack.Take(2));
        Assert.DoesNotContain(attack, e => e.Contains("charges"));
        Assert.Equal([SideId.P1, SideId.P2], battle.PendingRequests.Select(r => r.Side).Order());
    }

    [Fact]
    public void Solar_beam_fires_at_once_in_sun()
    {
        var battle = Singles([Mon("Venusaur", 100, moves: [SunnyDay, SolarBeam])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p1Move: 1);

        Assert.Equal(["P1.0 Venusaur uses solarbeam", "P2.0 Foe damaged"], events.Take(2));
    }

    [Fact]
    public void Skull_bash_raises_defense_while_charging()
    {
        var battle = Singles([Mon("Blastoise", 100, moves: [SkullBash])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(
            ["P1.0 Blastoise uses skullbash", "P1.0 Blastoise charges skullbash", "P1.0 Blastoise Defense +1 = 1"],
            events.Take(3));
    }

    [Theory]
    [InlineData("fly", "tackle", false)]
    [InlineData("fly", "gust", true)]
    [InlineData("fly", "thunder", true)]
    [InlineData("bounce", "skyuppercut", true)]
    [InlineData("dig", "earthquake", true)]
    [InlineData("dig", "tackle", false)]
    [InlineData("dive", "surf", true)]
    [InlineData("dive", "earthquake", false)]
    [InlineData("shadowforce", "thunder", false)]
    [InlineData("phantomforce", "earthquake", false)]
    public void Semi_invulnerable_pokemon_can_only_be_hit_by_some_moves(string chargeId, string foeMoveId, bool hits)
    {
        var foeMove = FoeMoves[foeMoveId];
        // Invulnerability is checked before accuracy: a move that cannot reach does not roll.
        var rolls = hits && foeMove.Accuracy is not null ? new PercentRolls(0) : new PercentRolls();
        var battle = Singles(
            [Mon("User", 100, moves: [ChargeMoves[chargeId]])], [Mon("Foe", 50, moves: [foeMove])], rolls);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(
            [$"P2.0 Foe uses {foeMoveId}", hits ? "P1.0 User damaged" : "P1.0 User avoided"],
            events.SkipWhile(e => !e.StartsWith("P2.0 Foe uses")).Take(2));
        Assert.Equal(0, rolls.Remaining);
    }

    [Theory]
    [InlineData("fly", "gust")]
    [InlineData("dig", "earthquake")]
    [InlineData("dive", "surf")]
    public void Moves_that_reach_a_semi_invulnerable_pokemon_deal_double_damage(string chargeId, string foeMoveId)
    {
        int DamageTo(MoveDefinition userMove)
        {
            var battle = Singles([Mon("User", 100, moves: [userMove])], [Mon("Foe", 50, moves: [FoeMoves[foeMoveId]])]);
            battle.Start();
            battle.Submit(SideId.P1, Move());
            return battle.Submit(SideId.P2, Move()).Events.OfType<DamageDealt>().Single(d => d.Side == SideId.P1).Amount;
        }

        Assert.Equal(2 * DamageTo(Splash), DamageTo(ChargeMoves[chargeId]));
    }

    [Fact]
    public void Bounce_doubles_the_base_power_of_gust_and_twister()
    {
        var gust = FoeMoves["gust"];
        var user = Mon("User", 100, moves: [ChargeMoves["bounce"]]);
        var foe = Mon("Foe", 50, moves: [gust]);
        var battle = Singles([user], [foe]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var dealt = battle.Submit(SideId.P2, Move()).Events.OfType<DamageDealt>().Single(d => d.Side == SideId.P1).Amount;

        var expected = new DamageCalculator(TestTypeChart.Gen6Sample)
            .Calculate(foe, user, gust with { Id = "fixed", BasePower = 80 }, false, new MaxRandom()).Damage;
        Assert.Equal(expected, dealt);
    }

    [Fact]
    public void Digging_and_diving_pokemon_take_no_sandstorm_damage()
    {
        var battle = Singles([Mon("User", 100, moves: [ChargeMoves["dig"]])], [Mon("Foe", 50, moves: [SandstormMove])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Foe hurt by Sandstorm 25", events);
        Assert.DoesNotContain(events, e => e.StartsWith("P1.0 User hurt"));
    }

    [Fact]
    public void Full_paralysis_on_the_second_turn_cancels_the_attack_and_frees_the_slot()
    {
        var rolls = new PercentRolls().Script(0, 4, 3, 0);
        var battle = Singles(
            [Mon("User", 100, moves: [Fly])], [Mon("Foe", 50, moves: [Paralyze, Splash])], rolls);
        battle.Start();
        Turn(battle);

        var second = OnlyP2(battle, p2Move: 1);

        Assert.Contains("P1.0 User can't move (Paralysis)", second);
        Assert.DoesNotContain(second, e => e.Contains("uses fly"));
        Assert.Contains(battle.PendingRequests, r => r.Side == SideId.P1);
        Assert.Equal(0, rolls.Remaining);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(90, false)]
    public void Hyper_beam_needs_a_turn_to_recharge_only_when_it_hits(int accuracyRoll, bool hits)
    {
        var battle = Singles(
            [Mon("Snorlax", 100, moves: [HyperBeam])], [Mon("Foe", 50, moves: [Splash])], new PercentRolls(accuracyRoll));
        battle.Start();

        Turn(battle);

        if (!hits)
        {
            Assert.Contains(battle.PendingRequests, r => r.Side == SideId.P1);
            return;
        }

        Assert.DoesNotContain(battle.PendingRequests, r => r.Side == SideId.P1);
        var recharge = OnlyP2(battle);
        Assert.Equal(["P1.0 Snorlax must recharge", "P2.0 Foe uses splash"], recharge.Take(2));
        Assert.Contains(battle.PendingRequests, r => r.Side == SideId.P1);
    }

    [Fact]
    public void A_turn_where_every_slot_is_locked_resolves_without_any_request()
    {
        var battle = Singles(
            [Mon("Snorlax", 100, moves: [HyperBeam])], [Mon("Venusaur", 50, moves: [SolarBeam])], new PercentRolls(0));
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(
            ["turn 2", "P1.0 Snorlax must recharge", "P2.0 Venusaur uses solarbeam", "P1.0 Snorlax damaged"],
            events.SkipWhile(e => e != "turn 2").Take(4));
        Assert.Contains("turn 3", events);
        Assert.Equal([SideId.P1, SideId.P2], battle.PendingRequests.Select(r => r.Side).Order());
    }

    [Fact]
    public void A_charged_attack_hits_the_slot_chosen_on_the_charging_turn()
    {
        var battle = Doubles(
            [Mon("User", 100, moves: [Fly]), Mon("Ally", 90, moves: [Splash])],
            [Mon("Left", 50, moves: [Splash]), Mon("Right", 40, moves: [Splash])]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 1), new MoveChoice(1, 0)));
        battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0)));

        Assert.Equal([1], battle.PendingRequests.Single(r => r.Side == SideId.P1).Slots);
        battle.Submit(SideId.P1, new Decision(new MoveChoice(1, 0)));
        var events = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0))).Events;

        Assert.Contains(events, e => e is DamageDealt { Side: SideId.P2, Slot: 1 });
    }
}
