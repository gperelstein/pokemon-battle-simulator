using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Moves that use, take or swap held items (Knock Off, Thief, Covet, Trick, Switcheroo, Fling,
/// Natural Gift, Acrobatics, Bug Bite, Pluck, Incinerate, Recycle), checked against Showdown's
/// data/moves.ts (no Gen 6 override changes them; gen7 Switcheroo only rewrites its failure check),
/// sim/pokemon.ts takeItem / setItem and the items' onTakeItem (Mega Stones stay with their species).
/// Base case: level 50, 100 Attack vs 100 Defense, max roll, every Pokémon Normal-type with 400 HP.
/// </summary>
public class ItemMoveTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition Growl = new(
        "growl", "Growl", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Boosts: [Stage(BoostStat.Attack, -1)]);

    internal static readonly MoveDefinition KnockOff = new(
        "knockoff", "Knock Off", PokemonType.Dark, MoveCategory.Physical, 65, Target: MoveTarget.Normal,
        Flags: MoveFlags.Contact);

    private static readonly MoveDefinition Thief = new(
        "thief", "Thief", PokemonType.Dark, MoveCategory.Physical, 60, Target: MoveTarget.Normal, Flags: MoveFlags.Contact);

    private static readonly MoveDefinition Covet = new(
        "covet", "Covet", PokemonType.Normal, MoveCategory.Physical, 60, Target: MoveTarget.Normal, Flags: MoveFlags.Contact);

    internal static readonly MoveDefinition Trick = new(
        "trick", "Trick", PokemonType.Psychic, MoveCategory.Status, 0, Target: MoveTarget.Normal);

    private static readonly MoveDefinition Switcheroo = new(
        "switcheroo", "Switcheroo", PokemonType.Dark, MoveCategory.Status, 0, Target: MoveTarget.Normal);

    internal static readonly MoveDefinition Fling = new(
        "fling", "Fling", PokemonType.Dark, MoveCategory.Physical, 0, Target: MoveTarget.Normal);

    private static readonly MoveDefinition NaturalGift = new(
        "naturalgift", "Natural Gift", PokemonType.Normal, MoveCategory.Physical, 0, Target: MoveTarget.Normal);

    private static readonly MoveDefinition Acrobatics = new(
        "acrobatics", "Acrobatics", PokemonType.Flying, MoveCategory.Physical, 55, Target: MoveTarget.Any,
        Flags: MoveFlags.Contact);

    private static readonly MoveDefinition BugBite = new(
        "bugbite", "Bug Bite", PokemonType.Bug, MoveCategory.Physical, 60, Target: MoveTarget.Normal, Flags: MoveFlags.Contact);

    private static readonly MoveDefinition Pluck = new(
        "pluck", "Pluck", PokemonType.Flying, MoveCategory.Physical, 60, Target: MoveTarget.Any, Flags: MoveFlags.Contact);

    private static readonly MoveDefinition Incinerate = new(
        "incinerate", "Incinerate", PokemonType.Fire, MoveCategory.Special, 60, Target: MoveTarget.AllAdjacentFoes);

    internal static readonly MoveDefinition Recycle = new(
        "recycle", "Recycle", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition Ember = new(
        "ember", "Ember", PokemonType.Fire, MoveCategory.Special, 50, Target: MoveTarget.Normal);

    /// <summary>Knock Off against a removable item: chainModify(1.5) on 65, (65 x 6144 + 2047) / 4096.</summary>
    private const int BoostedKnockOffPower = 97;

    private static readonly ItemDefinition Charizardite = new("charizarditex", FlingPower: 80, BoundSpecies: ["Charizard"]);

    internal static PokemonSet Holding(PokemonSet set, string item) => set with { Item = item };

    internal static PokemonSet Holding(PokemonSet set, ItemDefinition item) => set with { Item = item.Id, ItemData = item };

    private static IReadOnlyList<string> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return Describe(battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events);
    }

    private static IReadOnlyList<BattleEvent> RawTurn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events;
    }

    private static BattlePokemon Active(Battle battle, SideId side, int slot = 0) => battle.GetSide(side).Active[slot]!;

    private static int DamageTo(IEnumerable<BattleEvent> events, SideId side) =>
        events.OfType<DamageDealt>().First(d => d.Side == side).Amount;

    private static int PowerDamage(MoveDefinition move, int basePower, PokemonSet user, PokemonSet target) =>
        new DamageCalculator(TestTypeChart.Gen6Sample)
            .Calculate(user with { Item = null, ItemData = null }, target with { Item = null, ItemData = null },
                move with { BasePower = basePower }, false, new MaxRandom())
            .Damage;

    private static DecisionRequest Request(Battle battle, SideId side) =>
        battle.PendingRequests.Single(r => r.Side == side && r.Kind == DecisionKind.Turn);

    // ---- Knock Off (onBasePower x1.5 if the item can be taken; onAfterHit takeItem) ----

    [Fact]
    public void Knock_off_is_boosted_and_knocks_off_a_removable_item()
    {
        var user = Mon("User", 100, moves: [KnockOff]);
        var foe = Holding(Mon("Foe", 50, moves: [Splash]), "leftovers");
        var battle = Singles([user], [foe]);
        battle.Start();

        var events = RawTurn(battle);

        Assert.Equal(PowerDamage(KnockOff, BoostedKnockOffPower, user, foe), DamageTo(events, SideId.P2));
        Assert.Contains("P2.0 Foe leftovers removed by knockoff", Describe(events));
        Assert.Null(Active(battle, SideId.P2).Item);
        // Gone, so no Leftovers heal at the end of the turn.
        Assert.DoesNotContain(events, e => e is Healed);
    }

    [Fact]
    public void Knock_off_has_its_base_power_against_a_target_without_an_item()
    {
        var user = Mon("User", 100, moves: [KnockOff]);
        var foe = Mon("Foe", 50, moves: [Splash]);
        var battle = Singles([user], [foe]);
        battle.Start();

        var events = RawTurn(battle);

        Assert.Equal(PowerDamage(KnockOff, 65, user, foe), DamageTo(events, SideId.P2));
        Assert.DoesNotContain(events, e => e is ItemRemoved);
    }

    [Fact]
    public void Mega_stones_cannot_be_knocked_off_their_species_but_can_be_from_others()
    {
        var user = Mon("User", 100, moves: [KnockOff]);
        var charizard = Holding(Mon("Charizard", 50, moves: [Splash]), Charizardite);
        var battle = Singles([user], [charizard]);
        battle.Start();

        var events = RawTurn(battle);

        Assert.Equal(PowerDamage(KnockOff, 65, user, charizard), DamageTo(events, SideId.P2));
        Assert.Equal("charizarditex", Active(battle, SideId.P2).Item);

        var venusaur = Holding(Mon("Venusaur", 50, moves: [Splash]), Charizardite);
        battle = Singles([user], [venusaur]);
        battle.Start();
        events = RawTurn(battle);

        Assert.Equal(PowerDamage(KnockOff, BoostedKnockOffPower, user, venusaur), DamageTo(events, SideId.P2));
        Assert.Null(Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void Sticky_hold_keeps_the_item_but_knock_off_is_still_boosted()
    {
        var user = Mon("User", 100, moves: [KnockOff]);
        var foe = Holding(WithAbility(Mon("Muk", 50, moves: [Splash]), "stickyhold"), "leftovers");
        var battle = Singles([user], [foe]);
        battle.Start();

        var events = RawTurn(battle);

        Assert.Equal(PowerDamage(KnockOff, BoostedKnockOffPower, user, foe), DamageTo(events, SideId.P2));
        Assert.Contains("P2.0 Muk stickyhold activates", Describe(events));
        Assert.Equal("leftovers", Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void Mold_breaker_ignores_sticky_hold()
    {
        var user = WithAbility(Mon("User", 100, moves: [KnockOff]), "moldbreaker");
        var foe = Holding(WithAbility(Mon("Muk", 50, moves: [Splash]), "stickyhold"), "leftovers");
        var battle = Singles([user], [foe]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Muk leftovers removed by knockoff", events);
        Assert.DoesNotContain("P2.0 Muk stickyhold activates", events);
        Assert.Null(Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void Sticky_hold_does_not_protect_a_holder_that_was_knocked_out()
    {
        var user = Mon("User", 100, moves: [KnockOff]);
        var foe = Holding(WithAbility(Mon("Muk", 50, hp: 10, moves: [Splash]), "stickyhold"), "leftovers");
        var battle = Singles([user], [foe, Mon("Bench", 10, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P2.0 Muk fainted", "P2.0 Muk leftovers removed by knockoff"],
            events.Where(e => e.StartsWith("P2.0 Muk") && !e.Contains("damaged")));
        Assert.Null(battle.GetSide(SideId.P2).Team[0].Item);
    }

    [Fact]
    public void Losing_a_choice_item_ends_the_choice_lock()
    {
        var foe = Holding(Mon("Foe", 100, moves: [Tackle, QuickAttack]), "choiceband");
        var battle = Singles([Mon("User", 50, moves: [KnockOff])], [foe]);
        battle.Start();

        Turn(battle);

        Assert.Null(Active(battle, SideId.P2).Item);
        Assert.Empty(Request(battle, SideId.P2).DisabledMoves);
    }

    // ---- Thief and Covet (onAfterHit: only when the user holds nothing) ----

    [Theory]
    [InlineData("thief")]
    [InlineData("covet")]
    public void Thief_and_covet_steal_the_target_item_when_the_user_holds_nothing(string moveId)
    {
        var move = moveId == "thief" ? Thief : Covet;
        var battle = Singles([Mon("User", 100, moves: [move])], [Holding(Mon("Foe", 50, moves: [Splash]), "leftovers")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains($"P1.0 User steals leftovers from P2.0 ({moveId})", events);
        Assert.Equal("leftovers", Active(battle, SideId.P1).Item);
        Assert.Null(Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void Thief_steals_nothing_when_the_user_already_holds_an_item()
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Thief]), "choicescarf")], [Holding(Mon("Foe", 50, moves: [Splash]), "leftovers")]);
        battle.Start();

        var events = Turn(battle);

        Assert.DoesNotContain(events, e => e.Contains("steals"));
        Assert.Equal("choicescarf", Active(battle, SideId.P1).Item);
        Assert.Equal("leftovers", Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void Covet_steals_nothing_after_its_user_used_up_a_gem_for_it()
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Covet]), "normalgem")], [Holding(Mon("Foe", 50, moves: [Splash]), "leftovers")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P1.0 User normalgem consumed", events);
        Assert.DoesNotContain(events, e => e.Contains("steals"));
        Assert.Null(Active(battle, SideId.P1).Item);
        Assert.Equal("leftovers", Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void Sticky_hold_stops_thief()
    {
        var battle = Singles(
            [Mon("User", 100, moves: [Thief])],
            [Holding(WithAbility(Mon("Muk", 50, moves: [Splash]), "stickyhold"), "leftovers")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Muk stickyhold activates", events);
        Assert.Null(Active(battle, SideId.P1).Item);
        Assert.Equal("leftovers", Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void A_mega_stone_cannot_be_stolen_by_its_own_species()
    {
        var battle = Singles(
            [Mon("Charizard", 100, moves: [Thief])], [Holding(Mon("Foe", 50, moves: [Splash]), Charizardite)]);
        battle.Start();

        var events = Turn(battle);

        Assert.DoesNotContain(events, e => e.Contains("steals"));
        Assert.Equal("charizarditex", Active(battle, SideId.P2).Item);
    }

    // ---- Trick and Switcheroo ----

    [Theory]
    [InlineData("trick")]
    [InlineData("switcheroo")]
    public void Trick_and_switcheroo_swap_the_held_items(string moveId)
    {
        var move = moveId == "trick" ? Trick : Switcheroo;
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [move]), "choicescarf")], [Holding(Mon("Foe", 50, moves: [Splash]), "leftovers")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains($"P1.0 User {moveId}: holds leftovers, P2.0 Foe holds choicescarf", events);
        Assert.Equal("leftovers", Active(battle, SideId.P1).Item);
        Assert.Equal("choicescarf", Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void Trick_gives_the_only_item_to_the_other_side()
    {
        var battle = Singles([Mon("User", 100, moves: [Trick])], [Holding(Mon("Foe", 50, moves: [Splash]), "leftovers")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P1.0 User trick: holds leftovers, P2.0 Foe holds nothing", events);
        Assert.Equal("leftovers", Active(battle, SideId.P1).Item);
        Assert.Null(Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void Trick_fails_when_neither_holds_an_item()
    {
        var battle = Singles([Mon("User", 100, moves: [Trick])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Assert.Contains("P1.0 trick failed", Turn(battle));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sticky_hold_makes_its_holder_immune_to_trick_even_against_mold_breaker(bool moldBreaker)
    {
        var user = Holding(Mon("User", 100, moves: [Trick]), "choicescarf");
        var battle = Singles(
            [moldBreaker ? WithAbility(user, "moldbreaker") : user],
            [Holding(WithAbility(Mon("Muk", 50, moves: [Splash]), "stickyhold"), "leftovers")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Muk immune", events);
        Assert.Equal("choicescarf", Active(battle, SideId.P1).Item);
        Assert.Equal("leftovers", Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void Trick_fails_when_a_mega_stone_would_leave_or_reach_its_species()
    {
        var battle = Singles(
            [Holding(Mon("Charizard", 100, moves: [Trick]), Charizardite)], [Holding(Mon("Foe", 50, moves: [Splash]), "leftovers")]);
        battle.Start();

        Assert.Contains("P1.0 trick failed", Turn(battle));
        Assert.Equal("charizarditex", Active(battle, SideId.P1).Item);

        battle = Singles(
            [Holding(Mon("User", 100, moves: [Trick]), Charizardite)], [Holding(Mon("Charizard", 50, moves: [Splash]), "leftovers")]);
        battle.Start();

        Assert.Contains("P1.0 trick failed", Turn(battle));
        Assert.Equal("charizarditex", Active(battle, SideId.P1).Item);
        Assert.Equal("leftovers", Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void A_received_choice_item_resets_the_choice_lock()
    {
        // The foe is locked into Tackle by its Choice Scarf; the user, locked into Trick by its Choice
        // Band, swaps them. Each new Choice item's onStart drops its holder's lock.
        var battle = Singles(
            [Holding(Mon("User", 50, moves: [Trick, Splash]), "choiceband")],
            [Holding(Mon("Foe", 100, moves: [Tackle, QuickAttack]), "choicescarf")]);
        battle.Start();

        Turn(battle);

        Assert.Equal("choicescarf", Active(battle, SideId.P1).Item);
        Assert.Equal("choiceband", Active(battle, SideId.P2).Item);
        Assert.Empty(Request(battle, SideId.P1).DisabledMoves);
        Assert.Empty(Request(battle, SideId.P2).DisabledMoves);

        // The next move used locks afresh.
        Turn(battle, p1Move: 1, p2Move: 1);
        Assert.Equal([0], Request(battle, SideId.P1).DisabledMoves[0]);
        Assert.Equal([0], Request(battle, SideId.P2).DisabledMoves[0]);
    }

    [Fact]
    public void A_plate_boosts_its_type_for_whoever_holds_it()
    {
        var plate = new ItemDefinition("flameplate", PokemonType.Fire, FlingPower: 90);
        var user = Holding(Mon("User", 100, moves: [Trick, Ember]), plate);
        var foe = Mon("Foe", 50, moves: [Splash, Ember]);
        var battle = Singles([user], [foe]);
        battle.Start();
        Turn(battle);

        var events = RawTurn(battle, p1Move: 1, p2Move: 1);

        Assert.Equal(PowerDamage(Ember, 50, user, foe), DamageTo(events, SideId.P2));
        Assert.Equal(PowerDamage(Ember, 60, foe, user), DamageTo(events, SideId.P1));
    }

    // ---- Fling ----

    [Fact]
    public void Fling_uses_up_the_item_with_its_fling_power()
    {
        var user = Holding(Mon("User", 100, moves: [Fling]), new ItemDefinition("ironball", FlingPower: 130));
        var foe = Mon("Foe", 50, moves: [Splash]);
        var battle = Singles([user], [foe]);
        battle.Start();

        var events = RawTurn(battle);

        Assert.Equal(PowerDamage(Fling, 130, user, foe), DamageTo(events, SideId.P2));
        Assert.Equal(["P1.0 User uses fling", "P2.0 Foe damaged", "P1.0 User ironball consumed"], Describe(events).Take(3));
        Assert.Null(Active(battle, SideId.P1).Item);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("normalgem")] // no fling data: Fling fails
    public void Fling_fails_without_an_item_it_can_fling(string? item)
    {
        var user = Mon("User", 100, moves: [Fling]);
        var battle = Singles([item is null ? user : Holding(user, item)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P1.0 User uses fling", "P1.0 fling failed"], events.Take(2));
        Assert.Equal(item, Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void Fling_fails_with_the_users_own_mega_stone()
    {
        var battle = Singles([Holding(Mon("Charizard", 100, moves: [Fling]), Charizardite)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Assert.Contains("P1.0 fling failed", Turn(battle));
        Assert.Equal("charizarditex", Active(battle, SideId.P1).Item);
    }

    [Theory]
    [InlineData("toxicorb", MajorStatus.Toxic)]
    [InlineData("flameorb", MajorStatus.Burn)]
    [InlineData("lightball", MajorStatus.Paralysis)]
    [InlineData("poisonbarb", MajorStatus.Poison)]
    public void Fling_inflicts_the_status_of_its_item(string item, MajorStatus status)
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Fling]), new ItemDefinition(item, FlingPower: 30, FlingStatus: status))],
            [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains($"P2.0 Foe gets {status}", events);
        Assert.Equal(status, Active(battle, SideId.P2).Status);
    }

    [Fact]
    public void Shield_dust_blocks_the_status_of_a_flung_item()
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Fling]), new ItemDefinition("toxicorb", FlingPower: 30, FlingStatus: MajorStatus.Toxic))],
            [WithAbility(Mon("Foe", 50, moves: [Splash]), "shielddust")]);
        battle.Start();

        Turn(battle);

        Assert.Null(Active(battle, SideId.P2).Status);
        Assert.Null(Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void A_flung_berry_is_eaten_by_the_target()
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Fling]), new ItemDefinition(Berries.Liechi, FlingPower: 10))],
            [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P2.0 Foe damaged", "P2.0 Foe Attack +1 = 1", "P1.0 User liechiberry consumed"], events.Skip(1).Take(3));
        Assert.Equal(0, Active(battle, SideId.P1).GetStage(BoostStat.Attack));
    }

    [Fact]
    public void A_flung_white_herb_restores_the_targets_lowered_stats()
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Growl, Fling]), new ItemDefinition("whiteherb", FlingPower: 10))],
            [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        Turn(battle);
        Assert.Equal(-1, Active(battle, SideId.P2).GetStage(BoostStat.Attack));

        Turn(battle, p1Move: 1);

        Assert.Equal(0, Active(battle, SideId.P2).GetStage(BoostStat.Attack));
        Assert.Null(Active(battle, SideId.P1).Item);
    }

    // ---- Natural Gift and Acrobatics ----

    [Fact]
    public void Natural_gift_takes_its_type_and_power_from_the_berry_and_uses_it_up()
    {
        var oran = new ItemDefinition(Berries.Oran, FlingPower: 10, NaturalGiftType: PokemonType.Poison, NaturalGiftPower: 80);
        var user = Holding(Mon("User", 100, moves: [NaturalGift]), oran);
        var foe = Mon("Foe", 50, moves: [Splash]);
        var battle = Singles([user], [foe]);
        battle.Start();

        var events = RawTurn(battle);

        Assert.Equal(PowerDamage(NaturalGift with { Type = PokemonType.Poison }, 80, user, foe), DamageTo(events, SideId.P2));
        Assert.Equal(["P1.0 User uses naturalgift", "P1.0 User oranberry consumed", "P2.0 Foe damaged"],
            Describe(events).Take(3));
        Assert.Null(Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void Natural_gift_fails_without_a_berry()
    {
        var battle = Singles([Holding(Mon("User", 100, moves: [NaturalGift]), "leftovers")], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Assert.Equal(["P1.0 User uses naturalgift", "P1.0 naturalgift failed"], Turn(battle).Take(2));
        Assert.Equal("leftovers", Active(battle, SideId.P1).Item);
    }

    [Theory]
    [InlineData(null, 110)]
    [InlineData("leftovers", 55)]
    public void Acrobatics_doubles_its_power_without_an_item(string? item, int power)
    {
        var user = Mon("User", 100, moves: [Acrobatics]);
        var foe = Mon("Foe", 50, moves: [Splash]);
        var battle = Singles([item is null ? user : Holding(user, item)], [foe]);
        battle.Start();

        Assert.Equal(PowerDamage(Acrobatics, power, user, foe), DamageTo(RawTurn(battle), SideId.P2));
    }

    // ---- Bug Bite, Pluck and Incinerate ----

    [Theory]
    [InlineData("bugbite")]
    [InlineData("pluck")]
    public void Bug_bite_and_pluck_eat_the_targets_berry(string moveId)
    {
        var move = moveId == "bugbite" ? BugBite : Pluck;
        var battle = Singles([Mon("User", 100, moves: [move])], [Holding(Mon("Foe", 50, moves: [Splash]), Berries.Liechi)]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P2.0 Foe damaged", $"P2.0 Foe liechiberry removed by {moveId}", "P1.0 User Attack +1 = 1"],
            events.Skip(1).Take(3));
        Assert.Null(Active(battle, SideId.P2).Item);
        Assert.Null(Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void Bug_bite_leaves_items_that_are_not_berries()
    {
        var battle = Singles([Mon("User", 100, moves: [BugBite])], [Holding(Mon("Foe", 50, moves: [Splash]), "leftovers")]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("removed"));
        Assert.Equal("leftovers", Active(battle, SideId.P2).Item);
    }

    [Fact]
    public void Sticky_hold_stops_bug_bite()
    {
        var battle = Singles(
            [Mon("User", 100, moves: [BugBite])],
            [Holding(WithAbility(Mon("Muk", 50, moves: [Splash]), "stickyhold"), Berries.Liechi)]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Muk stickyhold activates", events);
        Assert.Equal(Berries.Liechi, Active(battle, SideId.P2).Item);
        Assert.Equal(0, Active(battle, SideId.P1).GetStage(BoostStat.Attack));
    }

    [Fact]
    public void Incinerate_burns_up_berries_and_gems_of_every_foe_it_hits()
    {
        var battle = Doubles(
            [Mon("User", 100, moves: [Incinerate]), Mon("Ally", 90, moves: [Splash])],
            [
                Holding(Mon("Foe", 50, moves: [Splash]), Berries.Liechi),
                Holding(Mon("Other", 40, moves: [Splash]), "normalgem"),
            ]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0)));
        var events = Describe(battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0))).Events);

        Assert.Contains("P2.0 Foe liechiberry removed by incinerate", events);
        Assert.Contains("P2.1 Other normalgem removed by incinerate", events);
        Assert.Null(Active(battle, SideId.P2).Item);
        Assert.Null(Active(battle, SideId.P2, 1).Item);
    }

    [Fact]
    public void Incinerate_leaves_other_items()
    {
        var battle = Singles([Mon("User", 100, moves: [Incinerate])], [Holding(Mon("Foe", 50, moves: [Splash]), "leftovers")]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("removed"));
        Assert.Equal("leftovers", Active(battle, SideId.P2).Item);
    }

    // ---- Recycle ----

    [Fact]
    public void Recycle_restores_the_last_item_the_user_used()
    {
        var battle = Singles([Holding(Mon("User", 100, moves: [Tackle, Recycle]), "normalgem")], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        Turn(battle);
        Assert.Null(Active(battle, SideId.P1).Item);

        var events = Turn(battle, p1Move: 1);

        Assert.Contains("P1.0 User receives normalgem", events);
        Assert.Equal("normalgem", Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void Recycle_fails_while_holding_an_item_or_without_a_used_one()
    {
        var battle = Singles([Holding(Mon("User", 100, moves: [Recycle]), "leftovers")], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        Assert.Contains("P1.0 recycle failed", Turn(battle));

        battle = Singles([Mon("User", 100, moves: [Recycle])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        Assert.Contains("P1.0 recycle failed", Turn(battle));
    }

    [Fact]
    public void Recycle_cannot_bring_back_a_knocked_off_item()
    {
        var battle = Singles(
            [Holding(Mon("User", 50, moves: [Recycle]), "leftovers")], [Mon("Foe", 100, moves: [KnockOff])]);
        battle.Start();
        Turn(battle);

        Assert.Contains("P1.0 recycle failed", Turn(battle));
        Assert.Null(Active(battle, SideId.P1).Item);
    }
}
