using Akka.Actor;
using Akka.TestKit.Xunit2;
using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Actors.Tests;

public class BattleActorTests : TestKit
{
    private static readonly MoveDefinition Tackle =
        new("tackle", "Tackle", PokemonType.Normal, MoveCategory.Physical, 50);

    private static readonly MoveDefinition VoltSwitch =
        new("voltswitch", "Volt Switch", PokemonType.Electric, MoveCategory.Special, 70, SelfSwitch: true);

    private static PokemonSet Mon(string species, int speed, int hp = 400, params MoveDefinition[] moves) =>
        new(species, [PokemonType.Normal], 50, new Stats(hp, 100, 100, 100, 100, speed),
            moves.Length == 0 ? [Tackle] : moves);

    private (IActorRef Battle, Akka.TestKit.TestProbe P1, Akka.TestKit.TestProbe P2) StartBattle(
        PokemonSet[] p1Team, PokemonSet[] p2Team)
    {
        var p1 = CreateTestProbe();
        var p2 = CreateTestProbe();
        var battle = Battle.Create(BattleFormat.Singles, p1Team, p2Team, new NeutralTypeChart(), new SeededBattleRandom(1));
        var actor = Sys.ActorOf(BattleActor.Props(battle, new Dictionary<SideId, IActorRef>
        {
            [SideId.P1] = p1.Ref,
            [SideId.P2] = p2.Ref,
        }));
        actor.Tell(new StartBattle());
        return (actor, p1, p2);
    }

    [Fact]
    public void Start_sends_each_player_the_events_and_only_its_own_request()
    {
        var (_, p1, p2) = StartBattle([Mon("Pikachu", 90)], [Mon("Onix", 70)]);

        var p1Update = p1.ExpectMsg<BattleUpdate>();
        var p2Update = p2.ExpectMsg<BattleUpdate>();

        Assert.Equal(p1Update.Events, p2Update.Events);
        Assert.Equal(SideId.P1, p1Update.Request!.Side);
        Assert.Equal(SideId.P2, p2Update.Request!.Side);
    }

    [Fact]
    public void Volt_switch_asks_only_the_user_for_a_mid_turn_replacement_and_rejects_the_opponent()
    {
        var (battle, p1, p2) = StartBattle(
            [Mon("Raichu", 110, moves: [VoltSwitch]), Mon("Jolteon", 130)],
            [Mon("Snorlax", 30)]);
        p1.ExpectMsg<BattleUpdate>();
        p2.ExpectMsg<BattleUpdate>();

        battle.Tell(new SubmitDecision(SideId.P1, new Decision(new MoveChoice(0, 0))), p1.Ref);
        p1.ExpectMsg<DecisionAccepted>();
        battle.Tell(new SubmitDecision(SideId.P2, new Decision(new MoveChoice(0, 0))), p2.Ref);

        var p1Update = p1.ExpectMsg<BattleUpdate>();
        p2.ExpectMsg<DecisionAccepted>();
        var p2Update = p2.ExpectMsg<BattleUpdate>();
        Assert.Equal(DecisionKind.MidTurnSwitch, p1Update.Request!.Kind);
        Assert.Null(p2Update.Request);

        battle.Tell(new SubmitDecision(SideId.P2, new Decision(new MoveChoice(0, 0))), p2.Ref);
        var rejected = p2.ExpectMsg<DecisionRejected>();
        Assert.Equal("P2 has no pending decision.", rejected.Reason);

        battle.Tell(new SubmitDecision(SideId.P1, new Decision(new SwitchChoice(0, 1))), p1.Ref);
        p1.ExpectMsg<DecisionAccepted>();
        var resumed = p1.ExpectMsg<BattleUpdate>();
        Assert.Contains(resumed.Events, e => e is SwitchedIn { Species: "Jolteon" });
        Assert.Contains(resumed.Events, e => e is MoveUsed { Species: "Snorlax" });
        Assert.Equal(DecisionKind.Turn, resumed.Request!.Kind);
    }

    [Fact]
    public void Actor_stops_after_the_battle_ends()
    {
        var (battle, p1, p2) = StartBattle([Mon("Jolteon", 130)], [Mon("Abra", 90, hp: 1)]);
        p1.ExpectMsg<BattleUpdate>();
        p2.ExpectMsg<BattleUpdate>();
        Watch(battle);

        battle.Tell(new SubmitDecision(SideId.P1, new Decision(new MoveChoice(0, 0))), p1.Ref);
        battle.Tell(new SubmitDecision(SideId.P2, new Decision(new MoveChoice(0, 0))), p2.Ref);

        p1.FishForMessage(m => m is BattleUpdate u && u.Events.Any(e => e is BattleEnded { Winner: SideId.P1 }));
        ExpectTerminated(battle);
    }

    private sealed class NeutralTypeChart : ITypeChart
    {
        public double Effectiveness(PokemonType attacking, IReadOnlyList<PokemonType> defending) => 1;
    }
}
