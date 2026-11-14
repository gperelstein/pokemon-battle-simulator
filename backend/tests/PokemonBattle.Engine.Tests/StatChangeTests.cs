using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

public class StatChangeTests
{
    private static readonly MoveDefinition SwordsDance = new(
        "swordsdance", "Swords Dance", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self,
        Boosts: [Stage(BoostStat.Attack, 2)]);

    private static readonly MoveDefinition Agility = new(
        "agility", "Agility", PokemonType.Psychic, MoveCategory.Status, 0, Target: MoveTarget.Self,
        Boosts: [Stage(BoostStat.Speed, 2)]);

    private static readonly MoveDefinition Growl = new(
        "growl", "Growl", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.AllAdjacentFoes,
        Accuracy: 100, Boosts: [Stage(BoostStat.Attack, -1)]);

    private static readonly MoveDefinition CloseCombat = new(
        "closecombat", "Close Combat", PokemonType.Fighting, MoveCategory.Physical, 120, Accuracy: 100,
        SelfBoosts: [Stage(BoostStat.Defense, -1), Stage(BoostStat.SpecialDefense, -1)]);

    private static readonly MoveDefinition Psychic = new(
        "psychic", "Psychic", PokemonType.Psychic, MoveCategory.Special, 90, Accuracy: 100,
        Secondaries: [new SecondaryEffect(10, TargetBoosts: [Stage(BoostStat.SpecialDefense, -1)])]);

    private static readonly MoveDefinition FlameCharge = new(
        "flamecharge", "Flame Charge", PokemonType.Fire, MoveCategory.Physical, 50, Accuracy: 100,
        Secondaries: [new SecondaryEffect(100, SelfBoosts: [Stage(BoostStat.Speed, 1)])]);

    private static IReadOnlyList<string> PlayTurn(Battle battle, Decision p1, Decision? p2 = null)
    {
        battle.Submit(SideId.P1, p1);
        return Describe(battle.Submit(SideId.P2, p2 ?? Move()).Events);
    }

    [Fact]
    public void Swords_dance_raises_attack_two_stages_and_later_hits_use_it()
    {
        var battle = Singles([Mon("Scyther", 105, moves: [SwordsDance, Tackle])], [Mon("Snorlax", 30)]);
        battle.Start();

        var setup = PlayTurn(battle, Move());
        battle.Submit(SideId.P1, Move(moveIndex: 1));
        var attack = battle.Submit(SideId.P2, Move()).Events;

        Assert.Equal(["P1.0 Scyther uses swordsdance", "P1.0 Scyther Attack +2 = 2"], setup.Take(2));
        Assert.Equal(2, battle.GetSide(SideId.P1).Team[0].GetStage(BoostStat.Attack));
        // Attack 200 against 100: 46, STAB 69 (36 unboosted).
        Assert.Contains(attack, e => e is DamageDealt { Side: SideId.P2, Amount: 69 });
    }

    [Fact]
    public void Stages_stop_at_plus_six()
    {
        var battle = Singles([Mon("Scyther", 105, moves: [SwordsDance])], [Mon("Snorlax", 30)]);
        battle.Start();

        PlayTurn(battle, Move());
        PlayTurn(battle, Move());
        var third = PlayTurn(battle, Move());
        var fourth = PlayTurn(battle, Move());

        Assert.Contains("P1.0 Scyther Attack +2 = 6", third);
        Assert.Contains("P1.0 Scyther Attack won't go higher", fourth);
        Assert.Equal(6, battle.GetSide(SideId.P1).Team[0].GetStage(BoostStat.Attack));
    }

    [Fact]
    public void Growl_lowers_the_attack_of_every_adjacent_foe()
    {
        var battle = Doubles(
            [Mon("Clefairy", 35, moves: [Growl]), Mon("Jigglypuff", 20)],
            [Mon("Rattata", 72), Mon("Pidgey", 56)]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0)));

        var events = Describe(battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0))).Events);

        Assert.Contains("P2.0 Rattata Attack -1 = -1", events);
        Assert.Contains("P2.1 Pidgey Attack -1 = -1", events);
    }

    [Fact]
    public void Self_drops_apply_after_the_hit_even_when_the_target_faints()
    {
        var battle = Singles(
            [Mon("Machamp", 55, moves: [CloseCombat])],
            [Mon("Rattata", 20, hp: 10), Mon("Pidgey", 56)]);
        battle.Start();

        var events = PlayTurn(battle, Move());

        Assert.Equal(
            [
                "P1.0 Machamp uses closecombat", "P2.0 Rattata damaged", "P2.0 Rattata fainted",
                "P1.0 Machamp Defense -1 = -1", "P1.0 Machamp SpecialDefense -1 = -1",
            ],
            events.Take(5));
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, false)]
    public void Secondary_stat_drops_follow_their_chance(int roll, bool lowered)
    {
        var rolls = new PercentRolls(roll);
        var battle = Singles([Mon("Mewtwo", 130, moves: [Psychic])], [Mon("Snorlax", 30)], rolls);
        battle.Start();

        var events = PlayTurn(battle, Move());

        Assert.Equal(lowered, events.Contains("P2.0 Snorlax SpecialDefense -1 = -1"));
        Assert.Equal(0, rolls.Remaining);
    }

    [Fact]
    public void Secondary_self_boosts_raise_the_user()
    {
        var battle = Singles([Mon("Ponyta", 90, moves: [FlameCharge])], [Mon("Snorlax", 30)]);
        battle.Start();

        var events = PlayTurn(battle, Move());

        Assert.Equal(["P2.0 Snorlax damaged", "P1.0 Ponyta Speed +1 = 1"], events.Skip(1).Take(2));
    }

    [Fact]
    public void Speed_stages_change_the_turn_order()
    {
        var battle = Singles([Mon("Slowbro", 30, moves: [Agility, Tackle])], [Mon("Rattata", 50)]);
        battle.Start();

        var first = PlayTurn(battle, Move());
        var second = PlayTurn(battle, Move(moveIndex: 1));

        Assert.Equal("P2.0 Rattata uses tackle", first[0]);
        Assert.Equal("P1.0 Slowbro uses tackle", second[0]);
    }

    [Fact]
    public void Stages_reset_when_the_pokemon_switches_out()
    {
        var battle = Singles(
            [Mon("Scyther", 105, moves: [SwordsDance]), Mon("Pinsir", 85)],
            [Mon("Snorlax", 30)]);
        battle.Start();

        PlayTurn(battle, Move());
        PlayTurn(battle, Switch(0, 1));

        Assert.Equal(0, battle.GetSide(SideId.P1).Team[0].GetStage(BoostStat.Attack));
    }
}
