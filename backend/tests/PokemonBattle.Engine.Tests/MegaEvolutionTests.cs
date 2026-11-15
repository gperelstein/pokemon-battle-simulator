using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Mega Evolution, checked against Showdown's sim/battle-actions.ts (canMegaEvo, runMegaEvo),
/// sim/battle-queue.ts (megaEvo order 104: after switches, before moves), sim/battle.ts (only Gen 7
/// re-sorts the mega's move, Gen 8+ re-sorts every action: Gen 6 keeps the speed computed when the
/// turn queue was built), sim/pokemon.ts formeChange / setSpecies / setAbility and sim/side.ts
/// (choice validation). Base case: level 50, every Pokémon Normal-type with 400 HP.
/// </summary>
public class MegaEvolutionTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition Fly = new(
        "fly", "Fly", PokemonType.Flying, MoveCategory.Physical, 90, Accuracy: 100, Charge: true);

    private static readonly MoveDefinition HyperBeam = new(
        "hyperbeam", "Hyper Beam", PokemonType.Normal, MoveCategory.Special, 150, Recharge: true);

    private static readonly MoveDefinition Spore = new(
        "spore", "Spore", PokemonType.Grass, MoveCategory.Status, 0, Target: MoveTarget.Normal, Status: MajorStatus.Sleep);

    private static readonly Stats MegaStats = new(999, 180, 150, 120, 110, 200);

    private static MegaForme Forme(string species, string ability = "toughclaws", params PokemonType[] types) =>
        new($"{species}-Mega", $"{species.ToLowerInvariant()}ite", types.Length == 0 ? [PokemonType.Fire] : types,
            MegaStats, ability, WeightHg: 1105, AbilityBreakable: true);

    /// <summary>A Pokémon holding its own Mega Stone, with the forme it Mega Evolves into.</summary>
    private static PokemonSet Megaable(string species, int speed, MegaForme? forme = null, params MoveDefinition[] moves)
    {
        var mega = forme ?? Forme(species);
        return Mon(species, speed, moves: moves.Length == 0 ? [Splash] : moves) with
        {
            Ability = "blaze", Item = mega.Item, Mega = mega,
            ItemData = new ItemDefinition(mega.Item, FlingPower: 80, BoundSpecies: [species]),
        };
    }

    private static Decision MegaMove(int slot = 0, int moveIndex = 0, int target = 0) =>
        new(new MoveChoice(slot, moveIndex, target, MegaEvolve: true));

    private static DecisionRequest Request(Battle battle, SideId side) => battle.PendingRequests.Single(r => r.Side == side);

    private static BattlePokemon Active(Battle battle, SideId side, int slot = 0) => battle.GetSide(side).Active[slot]!;

    private static IReadOnlyList<string> Turn(Battle battle, Decision p1, Decision p2)
    {
        Assert.True(battle.Submit(SideId.P1, p1).Accepted);
        var result = battle.Submit(SideId.P2, p2);
        Assert.True(result.Accepted, result.Error);
        return Describe(result.Events);
    }

    [Fact]
    public void A_holder_of_its_own_mega_stone_may_mega_evolve()
    {
        var battle = Singles([Megaable("Charizard", 100)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Assert.Equal([0], Request(battle, SideId.P1).MegaSlots);
        Assert.Empty(Request(battle, SideId.P2).MegaSlots);
    }

    [Fact]
    public void Holding_another_species_mega_stone_or_no_stone_gives_no_mega_evolution()
    {
        var foreignStone = Megaable("Charizard", 100) with { Species = "Venusaur" };
        var noStone = Megaable("Charizard", 100) with { Item = null, ItemData = null };
        var battle = Singles([foreignStone], [noStone]);
        battle.Start();

        Assert.Empty(Request(battle, SideId.P1).MegaSlots);
        Assert.Empty(Request(battle, SideId.P2).MegaSlots);
        var rejected = battle.Submit(SideId.P1, MegaMove());
        Assert.False(rejected.Accepted);
        Assert.Equal("Can't move: Venusaur can't Mega Evolve.", rejected.Error);
    }

    [Fact]
    public void Mega_evolution_changes_species_types_stats_ability_and_weight_and_keeps_hp()
    {
        var battle = Singles([Megaable("Charizard", 100)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle, MegaMove(), Move());

        Assert.Equal("P1.0 Charizard mega evolves into Charizard-Mega", events[0]);
        var mega = Active(battle, SideId.P1);
        Assert.Equal("Charizard-Mega", mega.Set.Species);
        Assert.Equal([PokemonType.Fire], mega.Set.Types);
        Assert.Equal(MegaStats with { Hp = 400 }, mega.Set.Stats);
        Assert.Equal(400, mega.CurrentHp);
        Assert.Equal("toughclaws", mega.Ability);
        Assert.Equal(1105, mega.WeightHg);
        Assert.Equal("charizardite", mega.Item);
    }

    [Fact]
    public void The_mega_evolved_event_names_the_species_before_and_the_forme_after()
    {
        var battle = Singles([Mon("Lead", 10), Megaable("Charizard", 100)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        battle.Submit(SideId.P1, Switch(0, 1));
        battle.Submit(SideId.P2, Move());

        battle.Submit(SideId.P1, MegaMove());
        var events = battle.Submit(SideId.P2, Move()).Events;

        Assert.Equal(new MegaEvolved(SideId.P1, 0, "Charizard", "Charizard-Mega"), events[0]);
    }

    [Fact]
    public void The_new_types_apply_to_moves_in_the_same_turn()
    {
        var battle = Singles([Megaable("Charizard", 10)], [Mon("Foe", 50, moves: [Surf])]);
        battle.Start();

        var events = battle.Submit(SideId.P1, MegaMove()) is { Accepted: true }
            ? battle.Submit(SideId.P2, Move()).Events
            : [];

        Assert.Contains(new Effectiveness(SideId.P1, 0, 2), events);
    }

    [Fact]
    public void Mega_evolution_happens_before_moves_and_after_switches()
    {
        var battle = Singles([Megaable("Charizard", 10)], [Mon("Foe", 50, moves: [QuickAttack]), Mon("Bench", 50)]);
        battle.Start();

        var events = Turn(battle, MegaMove(), Switch(0, 1));

        Assert.Equal(
            ["P2.0 out Foe", "P2.0 in Bench", "P1.0 Charizard mega evolves into Charizard-Mega",
             "P1.0 Charizard-Mega uses splash"],
            events.Take(4));
    }

    [Fact]
    public void Turn_order_uses_the_speed_from_before_mega_evolving_in_gen_6()
    {
        // Mega speed 200 would outspeed the foe's 100, but the turn was ordered with 50 (Gen 6).
        var battle = Singles([Megaable("Charizard", 50, moves: [Tackle])], [Mon("Foe", 100)]);
        battle.Start();

        var first = Turn(battle, MegaMove(), Move());
        var second = Turn(battle, Move(), Move());

        Assert.Equal(["P1.0 Charizard mega evolves into Charizard-Mega", "P2.0 Foe uses tackle"], first.Take(2));
        Assert.Equal("P1.0 Charizard-Mega uses tackle", second[0]);
    }

    [Fact]
    public void Mega_evolutions_of_both_sides_run_by_speed_before_any_move()
    {
        var battle = Singles([Megaable("Charizard", 50)], [Megaable("Venusaur", 100)]);
        battle.Start();

        var events = Turn(battle, MegaMove(), MegaMove());

        Assert.Equal(
            ["P2.0 Venusaur mega evolves into Venusaur-Mega", "P1.0 Charizard mega evolves into Charizard-Mega",
             "P2.0 Venusaur-Mega uses splash", "P1.0 Charizard-Mega uses splash"],
            events.Take(4));
    }

    [Fact]
    public void A_side_mega_evolves_once_per_battle()
    {
        var battle = Doubles(
            [Megaable("Charizard", 100), Megaable("Venusaur", 90), Megaable("Blastoise", 80)],
            [Mon("Foe", 50, moves: [Splash]), Mon("Foe2", 40, moves: [Splash])]);
        battle.Start();
        Assert.Equal([0, 1], Request(battle, SideId.P1).MegaSlots);

        var both = battle.Submit(SideId.P1, new Decision(
            new MoveChoice(0, 0, MegaEvolve: true), new MoveChoice(1, 0, MegaEvolve: true)));
        Assert.False(both.Accepted);
        Assert.Equal("Can't move: you can only Mega Evolve once per battle.", both.Error);

        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, MegaEvolve: true), new MoveChoice(1, 0)));
        battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0)));

        Assert.Empty(Request(battle, SideId.P1).MegaSlots);
        var again = battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0, MegaEvolve: true)));
        Assert.Equal("Can't move: Venusaur can't Mega Evolve.", again.Error);

        // A teammate sent in later cannot Mega Evolve either.
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new SwitchChoice(1, 2)));
        battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0)));
        Assert.Empty(Request(battle, SideId.P1).MegaSlots);
    }

    [Fact]
    public void The_forme_and_its_ability_stay_after_switching_out_and_run_again_on_switching_in()
    {
        var droughtForme = Forme("Charizard", "drought");
        var battle = Singles([Megaable("Charizard", 100, droughtForme), Mon("Bench", 10)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var mega = Turn(battle, MegaMove(), Move());
        Assert.Equal(
            ["P1.0 Charizard mega evolves into Charizard-Mega", "P1.0 Charizard-Mega drought activates", "weather Sun starts"],
            mega.Take(3));

        Turn(battle, Switch(0, 1), Move());
        var charizard = battle.GetSide(SideId.P1).Team[0];
        Assert.Equal("Charizard-Mega", charizard.Set.Species);
        Assert.Equal("drought", charizard.Ability);

        // Sun is still up, so Drought is silent; checking the switch-in by species is enough.
        var back = Turn(battle, Switch(0, 0), Move());
        Assert.Contains("P1.0 in Charizard-Mega", back);
        Assert.Empty(Request(battle, SideId.P1).MegaSlots);
    }

    [Fact]
    public void A_mega_trace_reverts_to_trace_not_to_the_base_ability_on_switching_out()
    {
        var traceForme = Forme("Alakazam", "trace", PokemonType.Psychic) with { AbilityBreakable = false, AbilityTraceable = false };
        var battle = Singles(
            [Megaable("Alakazam", 100, traceForme), Mon("Bench", 10)],
            [WithAbility(Mon("Foe", 50, moves: [Splash]), "thickfat")]);
        battle.Start();

        var events = Turn(battle, MegaMove(), Move());
        Assert.Contains("P1.0 Alakazam-Mega traced thickfat from P2.0", events);
        Assert.Equal("thickfat", Active(battle, SideId.P1).Ability);

        Turn(battle, Switch(0, 1), Move());
        Assert.Equal("trace", battle.GetSide(SideId.P1).Team[0].Ability);
    }

    [Fact]
    public void The_mega_abilitys_start_effects_and_updates_run_after_mega_evolving()
    {
        var moldBreaker = Forme("Gyarados", "moldbreaker", PokemonType.Water) with { AbilityBreakable = false };
        var battle = Singles([Megaable("Gyarados", 100, moldBreaker)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        Assert.Contains("P1.0 Gyarados-Mega moldbreaker activates", Turn(battle, MegaMove(), Move()));

        var insomnia = Forme("Mewtwo", "insomnia", PokemonType.Psychic);
        var sleeper = Singles([Megaable("Mewtwo", 10, insomnia)], [Mon("Foe", 50, moves: [Spore, Splash])]);
        sleeper.Start();
        Turn(sleeper, Move(), Move());
        Assert.Equal(MajorStatus.Sleep, Active(sleeper, SideId.P1).Status);

        // Sleep does not stop Mega Evolution; Insomnia then cures it (Showdown's Update after the action).
        var events = Turn(sleeper, MegaMove(), Move(moveIndex: 1));
        Assert.Equal(
            ["P1.0 Mewtwo mega evolves into Mewtwo-Mega", "P1.0 Mewtwo-Mega insomnia activates",
             "P1.0 Mewtwo-Mega cured of Sleep"],
            events.Take(3));
    }

    [Fact]
    public void Shadow_tag_from_mega_evolving_traps_foes_from_the_next_turn()
    {
        var shadowTag = Forme("Gengar", "shadowtag", PokemonType.Ghost, PokemonType.Poison) with { AbilityBreakable = false };
        var battle = Singles([Megaable("Gengar", 100, shadowTag)], [Mon("Foe", 50, moves: [Splash]), Mon("Bench", 10)]);
        battle.Start();
        Assert.Empty(Request(battle, SideId.P2).MaybeTrappedSlots);

        Turn(battle, MegaMove(), Move());

        Assert.Equal([0], Request(battle, SideId.P2).MaybeTrappedSlots);
    }

    [Fact]
    public void A_mega_forme_only_can_have_its_own_ability()
    {
        // Showdown reads the abilities of the current species: the forme's single ability.
        var dugtrio = Megaable("Dugtrio", 100) with { Ability = "sandveil", SpeciesAbilities = ["sandveil", "arenatrap", "sandforce"] };
        var battle = Singles([dugtrio], [Mon("Foe", 50, moves: [Splash]), Mon("Bench", 10)]);
        battle.Start();
        Assert.Equal([0], Request(battle, SideId.P2).MaybeTrappedSlots);

        Turn(battle, MegaMove(), Move());

        Assert.Empty(Request(battle, SideId.P2).MaybeTrappedSlots);
    }

    [Fact]
    public void The_forme_is_kept_when_fainting()
    {
        var battle = Singles([Megaable("Charizard", 10, moves: [Splash]), Mon("Bench", 10)],
            [Mon("Foe", 50, moves: [new MoveDefinition("ko", "KO", PokemonType.Normal, MoveCategory.Physical, 0, FixedDamage: 400)])]);
        battle.Start();

        var events = Turn(battle, MegaMove(), Move());

        Assert.Contains("P1.0 Charizard-Mega fainted", events);
        Assert.Equal("Charizard-Mega", battle.GetSide(SideId.P1).Team[0].Set.Species);
    }

    [Fact]
    public void A_slot_locked_into_its_action_gets_no_request_and_cannot_mega_evolve()
    {
        var battle = Singles([Megaable("Charizard", 100, moves: [HyperBeam])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Turn(battle, Move(), Move());
        Assert.DoesNotContain(battle.PendingRequests, r => r.Side == SideId.P1);
        var recharge = Describe(battle.Submit(SideId.P2, Move()).Events);
        Assert.DoesNotContain(recharge, e => e.Contains("mega evolves"));

        Assert.Equal([0], Request(battle, SideId.P1).MegaSlots);
    }

    [Fact]
    public void Mega_evolving_on_a_charge_turn_does_not_repeat_on_the_locked_turn()
    {
        var battle = Singles([Megaable("Charizard", 100, moves: [Fly])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var charge = Turn(battle, MegaMove(), Move());
        Assert.Equal(
            ["P1.0 Charizard mega evolves into Charizard-Mega", "P1.0 Charizard-Mega uses fly", "P1.0 Charizard-Mega charges fly"],
            charge.Take(3));

        var release = Describe(battle.Submit(SideId.P2, Move()).Events);
        Assert.DoesNotContain(release, e => e.Contains("mega evolves"));
        Assert.Contains("P1.0 Charizard-Mega uses fly", release);
    }

    [Fact]
    public void A_mega_request_cannot_choose_a_disabled_move_and_allies_keep_their_choice_lock()
    {
        var battle = Doubles(
            [Megaable("Charizard", 100, moves: [Splash]), Mon("Ally", 90, moves: [Tackle, QuickAttack]) with { Item = "choiceband" }],
            [Mon("Foe", 50, moves: [Splash]), Mon("Foe2", 40, moves: [Splash])]);
        battle.Start();

        Turn(battle,
            new Decision(new MoveChoice(0, 0, MegaEvolve: true), new MoveChoice(1, 0)),
            new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0)));

        var request = Request(battle, SideId.P1);
        Assert.Equal([1], request.DisabledMoves[1]);
        var rejected = battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 1)));
        Assert.Equal("Can't move: Ally's Quick Attack is disabled.", rejected.Error);
    }

    [Fact]
    public void The_mega_stone_still_cannot_be_knocked_off_once_mega_evolved()
    {
        var knockOff = new MoveDefinition(
            "knockoff", "Knock Off", PokemonType.Dark, MoveCategory.Physical, 65, Target: MoveTarget.Normal, Flags: MoveFlags.Contact);
        var battle = Singles([Megaable("Charizard", 100)], [Mon("Foe", 50, moves: [knockOff])]);
        battle.Start();

        var events = Turn(battle, MegaMove(), Move());

        Assert.DoesNotContain(events, e => e.Contains("removed by knockoff"));
        Assert.Equal("charizardite", Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void A_mega_flag_is_only_valid_on_a_move_of_a_turn_request()
    {
        var battle = Singles([Megaable("Charizard", 100), Mon("Bench", 10)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        // Choosing a switch never Mega Evolves; the stone holder keeps its chance for later.
        Turn(battle, Switch(0, 1), Move());
        Turn(battle, Switch(0, 0), Move());

        Assert.Equal([0], Request(battle, SideId.P1).MegaSlots);
        Assert.Equal("Charizard", Active(battle, SideId.P1).Set.Species);
    }

    [Fact]
    public void Move_choices_without_the_flag_and_requests_without_megas_are_unchanged()
    {
        var plain = new MoveChoice(0, 1, 0);
        Assert.False(plain.MegaEvolve);
        Assert.Equal(new MoveChoice(0, 1), plain);

        var battle = Singles([Mon("Snorlax", 100)], [Mon("Foe", 50)]);
        battle.Start();
        var request = Request(battle, SideId.P1);
        Assert.Empty(request.MegaSlots);
        Assert.Equal(new DecisionRequest(SideId.P1, DecisionKind.Turn, request.Slots, request.TrappedSlots, request.DisabledMoves), request);
    }
}
