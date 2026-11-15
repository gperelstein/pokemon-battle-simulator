using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Forced switches (Roar, Whirlwind, Dragon Tail, Circle Throw, Red Card) and Eject Button, checked
/// against Showdown's sim/battle-actions.ts (forceSwitch, dragIn, switchIn, runMoveEffects' "-fail"
/// for a status move with nothing to drag in, the AfterMoveSecondary event skipped by Sheer Force),
/// sim/battle.ts runAction (phazing first, then faints, then switch requests; getRandomSwitchable)
/// and data/items.ts (Red Card, Eject Button, with no Gen 6 override). Every Pokémon has 400 HP.
/// </summary>
public class ForcedSwitchTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition Roar = new(
        "roar", "Roar", PokemonType.Normal, MoveCategory.Status, 0, Priority: -6, Target: MoveTarget.Normal,
        Flags: MoveFlags.Sound, ForceSwitch: true);

    private static readonly MoveDefinition DragonTail = new(
        "dragontail", "Dragon Tail", PokemonType.Dragon, MoveCategory.Physical, 60, Priority: -6,
        Target: MoveTarget.Normal, Flags: MoveFlags.Contact, ForceSwitch: true);

    private static readonly MoveDefinition UTurn = new(
        "uturn", "U-turn", PokemonType.Bug, MoveCategory.Physical, 70, SelfSwitch: true, Flags: MoveFlags.Contact);

    private static readonly MoveDefinition Growl = new(
        "growl", "Growl", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Boosts: [Stage(BoostStat.Attack, -1)]);

    /// <summary>A damaging move with a secondary effect the engine does not model (Sheer Force boosts it).</summary>
    private static readonly MoveDefinition Bite = new(
        "bite", "Bite", PokemonType.Dark, MoveCategory.Physical, 60, Flags: MoveFlags.Contact, HasSecondaryEffects: true);

    private static PokemonSet Holding(PokemonSet set, string item) => set with { Item = item };

    private static void PlayTurn(Battle battle, Decision p1, Decision p2)
    {
        Assert.True(battle.Submit(SideId.P1, p1).Accepted);
        Assert.True(battle.Submit(SideId.P2, p2).Accepted);
    }

    private static IReadOnlyList<string> Turn(Battle battle, Decision p1, Decision p2)
    {
        Assert.True(battle.Submit(SideId.P1, p1).Accepted);
        var result = battle.Submit(SideId.P2, p2);
        Assert.True(result.Accepted);
        return Describe(result.Events);
    }

    [Fact]
    public void Roar_drags_in_a_random_healthy_foe_without_asking_its_player()
    {
        // Bench [Second, Third]: the random pick 0 drags in Second.
        var battle = Singles(
            [Mon("Roarer", 100, moves: [Roar])],
            [Mon("Lead", 50, moves: [Splash]), Mon("Second", 40), Mon("Third", 30)],
            new PercentRolls().Script(0, 2, 0));
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.Equal(
            [
                "P2.0 Lead uses splash", "P1.0 Roarer uses roar",
                "P2.0 Lead dragged out", "P2.0 out Lead", "P2.0 in Second",
                "turn 2", "request P1 Turn [0]", "request P2 Turn [0]",
            ],
            events);
    }

    [Fact]
    public void The_dragged_in_pokemon_is_picked_among_the_whole_healthy_bench()
    {
        var battle = Singles(
            [Mon("Roarer", 100, moves: [Roar])],
            [Mon("Lead", 50, moves: [Splash]), Mon("Fainted", 40, hp: 0), Mon("Second", 40), Mon("Third", 30)],
            new PercentRolls().Script(0, 2, 1));
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.Contains("P2.0 in Third", events);
        Assert.Equal("Third", battle.GetSide(SideId.P2).Active[0]!.Set.Species);
    }

    [Fact]
    public void Roar_fails_when_the_target_has_no_healthy_ally_to_drag_in()
    {
        var battle = Singles([Mon("Roarer", 100, moves: [Roar])], [Mon("Lead", 50, moves: [Splash]), Mon("Fainted", 40, hp: 0)]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.Contains("P1.0 roar failed", events);
        Assert.DoesNotContain("P2.0 out Lead", events);
    }

    [Fact]
    public void The_dragged_in_pokemon_runs_its_switch_in_ability()
    {
        var battle = Singles(
            [Mon("Roarer", 100, moves: [Roar])],
            [Mon("Lead", 50, moves: [Splash]), WithAbility(Mon("Gyarados", 40), "intimidate")]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.Equal(
            ["P2.0 in Gyarados", "P2.0 Gyarados intimidate activates", "P1.0 Roarer Attack -1 = -1"],
            events.SkipWhile(e => e != "P2.0 in Gyarados").Take(3));
    }

    [Fact]
    public void Dragon_tail_damages_then_drags_the_target_out()
    {
        var battle = Singles([Mon("Dragon", 100, moves: [DragonTail])], [Mon("Lead", 50, moves: [Splash]), Mon("Second", 40)]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.Equal(
            ["P1.0 Dragon uses dragontail", "P2.0 Lead damaged", "P2.0 Lead dragged out", "P2.0 out Lead", "P2.0 in Second"],
            events.SkipWhile(e => e != "P1.0 Dragon uses dragontail").Take(5));
    }

    [Fact]
    public void Dragon_tail_still_hits_without_failing_when_there_is_nothing_to_drag_in()
    {
        var battle = Singles([Mon("Dragon", 100, moves: [DragonTail])], [Mon("Lead", 50, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.Contains("P2.0 Lead damaged", events);
        Assert.DoesNotContain("P1.0 dragontail failed", events);
        Assert.DoesNotContain("P2.0 Lead dragged out", events);
    }

    [Fact]
    public void Dragon_tail_does_not_drag_a_target_it_knocked_out()
    {
        var battle = Singles(
            [Mon("Dragon", 100, moves: [DragonTail])], [Mon("Lead", 50, hp: 10, moves: [Splash]), Mon("Second", 40)]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.DoesNotContain("P2.0 Lead dragged out", events);
        Assert.Contains("request P2 FaintReplacement [0]", events);
    }

    [Fact]
    public void Red_card_drags_out_the_attacker_that_hit_its_holder()
    {
        var battle = Singles(
            [Mon("Attacker", 100, moves: [Tackle]), Mon("Bench", 10)],
            [Holding(Mon("Holder", 50, moves: [Splash]), "redcard")]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.Equal(
            [
                "P1.0 Attacker uses tackle", "P2.0 Holder damaged", "P2.0 Holder redcard consumed",
                "P1.0 Attacker dragged out", "P1.0 out Attacker", "P1.0 in Bench",
            ],
            events.SkipWhile(e => e != "P1.0 Attacker uses tackle").Take(6));
        Assert.DoesNotContain(events, e => e.Contains("MidTurnSwitch"));
        Assert.Null(battle.GetSide(SideId.P2).Active[0]!.Item);
    }

    [Fact]
    public void Shell_bell_does_not_heal_an_attacker_that_red_card_drags_out()
    {
        // Showdown's shellbell onAfterMoveSecondarySelf requires !pokemon.forceSwitchFlag, which Red
        // Card sets during AfterMoveSecondary (data_items.ts shellbell / redcard; no Gen 6 override).
        var battle = Singles(
            [Holding(Mon("Attacker", 100, moves: [Tackle]), "shellbell"), Mon("Bench", 10)],
            [Holding(Mon("Holder", 150, moves: [Tackle]), "redcard")]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.Contains("P1.0 Attacker dragged out", events);
        Assert.DoesNotContain(events, e => e.Contains("shellbell") || e.StartsWith("P1.0 Attacker healed"));
    }

    [Fact]
    public void Red_card_ignores_status_moves()
    {
        var battle = Singles(
            [Mon("Attacker", 100, moves: [Growl]), Mon("Bench", 10)],
            [Holding(Mon("Holder", 50, moves: [Splash]), "redcard")]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.DoesNotContain("P2.0 Holder redcard consumed", events);
        Assert.Equal("redcard", battle.GetSide(SideId.P2).Active[0]!.Item);
    }

    [Fact]
    public void Red_card_stays_when_the_attacker_has_no_ally_to_switch_to()
    {
        var battle = Singles([Mon("Attacker", 100, moves: [Tackle])], [Holding(Mon("Holder", 50, moves: [Splash]), "redcard")]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.DoesNotContain("P2.0 Holder redcard consumed", events);
        Assert.Equal("redcard", battle.GetSide(SideId.P2).Active[0]!.Item);
    }

    [Fact]
    public void Red_card_does_not_act_when_its_holder_faints()
    {
        var battle = Singles(
            [Mon("Attacker", 100, moves: [Tackle]), Mon("Bench", 10)],
            [Holding(Mon("Holder", 50, hp: 10, moves: [Splash]), "redcard"), Mon("Spare", 10)]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.DoesNotContain("P1.0 Attacker dragged out", events);
    }

    [Fact]
    public void Sheer_force_suppresses_red_card_and_eject_button_on_moves_with_secondaries()
    {
        var attacker = WithAbility(Mon("Attacker", 100, moves: [Bite]), "sheerforce");
        foreach (var item in new[] { "redcard", "ejectbutton" })
        {
            var battle = Singles([attacker, Mon("Bench", 10)], [Holding(Mon("Holder", 50, moves: [Splash]), item), Mon("Spare", 10)]);
            battle.Start();

            var events = Turn(battle, Move(), Move());

            Assert.DoesNotContain($"P2.0 Holder {item} consumed", events);
            Assert.Equal(item, battle.GetSide(SideId.P2).Active[0]!.Item);
        }
    }

    [Fact]
    public void Red_card_against_u_turn_drags_the_attacker_out_and_cancels_its_switch()
    {
        var battle = Singles(
            [Mon("Pivot", 100, moves: [UTurn]), Mon("Second", 10), Mon("Third", 10)],
            [Holding(Mon("Holder", 50, moves: [Splash]), "redcard")],
            new PercentRolls().Script(0, 2, 0));
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.Contains("P1.0 in Second", events);
        Assert.DoesNotContain(events, e => e.Contains("MidTurnSwitch"));
        Assert.Contains("request P1 Turn [0]", events);
    }

    [Fact]
    public void Dragon_tail_on_a_red_card_holder_drags_the_holder_and_keeps_the_card()
    {
        var battle = Singles(
            [Mon("Dragon", 100, moves: [DragonTail]), Mon("Bench", 10)],
            [Holding(Mon("Holder", 50, moves: [Splash]), "redcard"), Mon("Second", 40)]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.Contains("P2.0 Holder dragged out", events);
        Assert.DoesNotContain("P2.0 Holder redcard consumed", events);
        Assert.Equal("redcard", battle.GetSide(SideId.P2).Team[0].Item);
    }

    [Fact]
    public void Eject_button_asks_its_holders_player_for_a_replacement()
    {
        var battle = Singles(
            [Mon("Attacker", 100, moves: [Tackle])],
            [Holding(Mon("Holder", 50, moves: [Splash]), "ejectbutton"), Mon("Second", 40)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var result = battle.Submit(SideId.P2, Move());

        Assert.Equal(
            ["P1.0 Attacker uses tackle", "P2.0 Holder damaged", "P2.0 Holder ejectbutton consumed", "request P2 MidTurnSwitch [0]"],
            Describe(result.Events));
        var resumed = battle.Submit(SideId.P2, Switch(0, 1));
        Assert.True(resumed.Accepted);
        Assert.Equal(["P2.0 out Holder", "P2.0 in Second", "turn 2"], Describe(resumed.Events).Take(3));
    }

    [Fact]
    public void A_pokemon_switched_out_by_eject_button_loses_its_queued_move()
    {
        var battle = Singles(
            [Mon("Attacker", 100, moves: [Tackle])],
            [Holding(Mon("Holder", 50, moves: [Tackle]), "ejectbutton"), Mon("Second", 40)]);
        battle.Start();
        PlayTurn(battle, Move(), Move());

        var resumed = Describe(battle.Submit(SideId.P2, Switch(0, 1)).Events);

        Assert.DoesNotContain(resumed, e => e.Contains("uses tackle"));
    }

    [Fact]
    public void Eject_button_takes_over_the_attackers_u_turn_switch()
    {
        var battle = Singles(
            [Mon("Pivot", 100, moves: [UTurn]), Mon("Second", 10)],
            [Holding(Mon("Holder", 50, moves: [Splash]), "ejectbutton"), Mon("Spare", 40)]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        battle.Submit(SideId.P2, Move());

        var pending = Assert.Single(battle.PendingRequests);
        Assert.Equal((SideId.P2, DecisionKind.MidTurnSwitch), (pending.Side, pending.Kind));
    }

    [Fact]
    public void Eject_button_does_nothing_when_its_holder_faints_or_has_no_ally()
    {
        var fainting = Singles(
            [Mon("Attacker", 100, moves: [Tackle])],
            [Holding(Mon("Holder", 50, hp: 10, moves: [Splash]), "ejectbutton"), Mon("Second", 40)]);
        fainting.Start();
        var alone = Singles([Mon("Attacker", 100, moves: [Tackle])], [Holding(Mon("Holder", 50, moves: [Splash]), "ejectbutton")]);
        alone.Start();

        var faintEvents = Turn(fainting, Move(), Move());
        var aloneEvents = Turn(alone, Move(), Move());

        Assert.DoesNotContain("P2.0 Holder ejectbutton consumed", faintEvents);
        Assert.Contains("request P2 FaintReplacement [0]", faintEvents);
        Assert.DoesNotContain("P2.0 Holder ejectbutton consumed", aloneEvents);
        Assert.Equal("ejectbutton", alone.GetSide(SideId.P2).Active[0]!.Item);
    }

    [Fact]
    public void Dragon_tail_on_an_eject_button_holder_drags_it_and_keeps_the_button()
    {
        var battle = Singles(
            [Mon("Dragon", 100, moves: [DragonTail])],
            [Holding(Mon("Holder", 50, moves: [Splash]), "ejectbutton"), Mon("Second", 40)]);
        battle.Start();

        var events = Turn(battle, Move(), Move());

        Assert.Contains("P2.0 Holder dragged out", events);
        Assert.DoesNotContain(events, e => e.Contains("MidTurnSwitch"));
        Assert.Equal("ejectbutton", battle.GetSide(SideId.P2).Team[0].Item);
    }

    [Fact]
    public void Only_the_fastest_eject_button_holder_switches_after_a_spread_move()
    {
        var battle = Doubles(
            [Mon("Attacker", 100, moves: [Surf]), Mon("Ally", 90, moves: [Splash])],
            [
                Holding(Mon("Slow", 30, moves: [Splash]), "ejectbutton"),
                Holding(Mon("Fast", 60, moves: [Splash]), "ejectbutton"),
                Mon("Spare", 10),
            ]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0)));

        var events = Describe(battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0))).Events);

        Assert.Contains("P2.1 Fast ejectbutton consumed", events);
        Assert.DoesNotContain("P2.0 Slow ejectbutton consumed", events);
        Assert.Equal("request P2 MidTurnSwitch [1]", events[^1]);
    }

    [Fact]
    public void A_red_card_drag_happens_before_an_eject_button_request_from_the_same_move()
    {
        var battle = Doubles(
            [Mon("Attacker", 100, moves: [Surf]), Mon("Ally", 90, moves: [Splash]), Mon("Bench", 10)],
            [
                Holding(Mon("Carded", 30, moves: [Splash]), "redcard"),
                Holding(Mon("Ejector", 60, moves: [Splash]), "ejectbutton"),
                Mon("Spare", 10),
            ]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0)));

        var events = Describe(battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0))).Events);

        Assert.Equal(
            [
                "P2.1 Ejector ejectbutton consumed", "P2.0 Carded redcard consumed",
                "P1.0 Attacker dragged out", "P1.0 out Attacker", "P1.0 in Bench", "request P2 MidTurnSwitch [1]",
            ],
            events.SkipWhile(e => e != "P2.1 Ejector ejectbutton consumed"));
    }
}
