using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Passive held items in battle (base power, recoil, speed, turn order, accuracy, grounding),
/// checked against Showdown's data/items.ts with the Gen 6 mods (Life Orb from gen6), the gem
/// condition (data/conditions.ts, x1.3), sim/battle-queue.ts (fractional priority) and
/// sim/pokemon.ts (getStat, isGrounded). Expected values come from an independent mirror of
/// Showdown's formula. Base case: level 50, 100 Attack vs 100 Defense, Tackle (50, STAB), max roll: 36.
/// </summary>
public class HeldItemBattleTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static PokemonSet Holding(PokemonSet set, string item) => set with { Item = item };

    private static IReadOnlyList<BattleEvent> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events;
    }

    private static int DamageTo(IEnumerable<BattleEvent> events, SideId side) =>
        events.OfType<DamageDealt>().Last(d => d.Side == side).Amount;

    private static int PowerDamage(MoveDefinition move, int basePower, PokemonSet user, PokemonSet target) =>
        new DamageCalculator(TestTypeChart.Gen6Sample)
            .Calculate(user with { Item = null }, target, move with { BasePower = basePower }, false, new MaxRandom())
            .Damage;

    private static string FirstMover(IEnumerable<BattleEvent> events) => events.OfType<MoveUsed>().First().Species;

    [Theory]
    [InlineData("charcoal", PokemonType.Fire, 60)] // 4915/4096
    [InlineData("charcoal", PokemonType.Water, 50)]
    [InlineData("mysticwater", PokemonType.Water, 60)]
    [InlineData("seaincense", PokemonType.Water, 60)]
    [InlineData("waveincense", PokemonType.Water, 60)]
    [InlineData("hardstone", PokemonType.Rock, 60)]
    [InlineData("rockincense", PokemonType.Rock, 60)]
    [InlineData("silkscarf", PokemonType.Normal, 60)]
    [InlineData("blackbelt", PokemonType.Fighting, 60)]
    [InlineData("oddincense", PokemonType.Psychic, 60)]
    [InlineData("roseincense", PokemonType.Grass, 60)]
    [InlineData("dragonfang", PokemonType.Dragon, 60)]
    public void Type_boosting_items_raise_the_power_of_their_type(string item, PokemonType type, int expectedPower)
    {
        var move = new MoveDefinition("strike", "Strike", type, MoveCategory.Physical, 50);
        var user = Holding(Mon("User", 100, moves: [move]), item);
        var target = Mon("Target", 50, moves: [Splash]);
        var battle = Singles([user], [target]);
        battle.Start();

        Assert.Equal(PowerDamage(move, expectedPower, user, target), DamageTo(Turn(battle), SideId.P2));
    }

    [Theory]
    [InlineData(PokemonType.Fire, 60)]
    [InlineData(PokemonType.Water, 50)]
    public void Plates_raise_the_power_of_their_type(PokemonType type, int expectedPower)
    {
        var move = new MoveDefinition("strike", "Strike", type, MoveCategory.Physical, 50);
        var user = Holding(Mon("User", 100, moves: [move]), "flameplate") with { PlateType = PokemonType.Fire };
        var target = Mon("Target", 50, moves: [Splash]);
        var battle = Singles([user], [target]);
        battle.Start();

        Assert.Equal(PowerDamage(move, expectedPower, user, target), DamageTo(Turn(battle), SideId.P2));
    }

    [Theory]
    [InlineData("muscleband", MoveCategory.Physical, 55)] // 4505/4096
    [InlineData("muscleband", MoveCategory.Special, 50)]
    [InlineData("wiseglasses", MoveCategory.Special, 55)]
    [InlineData("wiseglasses", MoveCategory.Physical, 50)]
    public void Muscle_band_and_wise_glasses_boost_their_category(string item, MoveCategory category, int expectedPower)
    {
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, category, 50);
        var user = Holding(Mon("User", 100, moves: [move]), item);
        var target = Mon("Target", 50, moves: [Splash]);
        var battle = Singles([user], [target]);
        battle.Start();

        Assert.Equal(PowerDamage(move, expectedPower, user, target), DamageTo(Turn(battle), SideId.P2));
    }

    [Fact]
    public void Technician_checks_the_power_before_type_boosting_items()
    {
        // BasePower chain: Technician (priority 19) sees 60 and applies x1.5, then Charcoal (15) x1.2:
        // 6144 x 4915 = 7373/4096, power 108. Charcoal first would have made it 72, too high for Technician.
        var move = new MoveDefinition("strike", "Strike", PokemonType.Fire, MoveCategory.Physical, 60);
        var user = WithAbility(Holding(Mon("User", 100, moves: [move]), "charcoal"), "technician");
        var target = Mon("Target", 50, moves: [Splash]);
        var battle = Singles([user], [target]);
        battle.Start();

        Assert.Equal(PowerDamage(move, 108, user with { Ability = null }, target), DamageTo(Turn(battle), SideId.P2));
    }

    [Fact]
    public void Normal_gem_boosts_the_first_normal_move_and_is_consumed()
    {
        var user = Holding(Mon("User", 100, moves: [Tackle]), "normalgem");
        var battle = Singles([user], [Mon("Target", 50, moves: [Splash])]);
        battle.Start();

        // Gem x1.3 (5325/4096): power 65, damage 45 with STAB; announced before the hit.
        var first = Turn(battle);
        Assert.Equal(45, DamageTo(first, SideId.P2));
        Assert.Equal(
            ["P1.0 User uses tackle", "P1.0 User normalgem consumed", "P2.0 Target damaged"],
            Describe(first.Where(e => e is MoveUsed { Side: SideId.P1 } or ItemConsumed or DamageDealt)));
        Assert.Null(battle.GetSide(SideId.P1).Active[0]!.Item);

        Assert.Equal(36, DamageTo(Turn(battle), SideId.P2));
    }

    [Theory]
    [InlineData(PokemonType.Fire, MoveCategory.Physical)]
    [InlineData(PokemonType.Normal, MoveCategory.Status)]
    public void Normal_gem_ignores_other_types_and_status_moves(PokemonType type, MoveCategory category)
    {
        var move = new MoveDefinition("strike", "Strike", type, category, category == MoveCategory.Status ? 0 : 50);
        var user = Holding(Mon("User", 100, moves: [move]), "normalgem");
        var battle = Singles([user], [Mon("Target", 50, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle);
        Assert.Empty(events.OfType<ItemConsumed>());
        Assert.Equal("normalgem", battle.GetSide(SideId.P1).Active[0]!.Item);
    }

    [Fact]
    public void Life_orb_boosts_damage_and_costs_a_tenth_of_max_hp()
    {
        var user = Holding(Mon("User", 100, moves: [Tackle]), "lifeorb");
        var battle = Singles([user], [Mon("Target", 50, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle);
        // 36 x 5324/4096 = 47; recoil floor(400 / 10) = 40, after the hit.
        Assert.Equal(47, DamageTo(events, SideId.P2));
        var recoil = events.OfType<ItemDamage>().Single();
        Assert.Equal((SideId.P1, "lifeorb", 40, 360), (recoil.Side, recoil.Item, recoil.Amount, recoil.RemainingHp));
        Assert.True(events.ToList().IndexOf(recoil) > events.ToList().IndexOf(events.OfType<DamageDealt>().Single()));
    }

    [Theory]
    [InlineData("miss")] // the move did not hit anything
    [InlineData("magicguard")] // Magic Guard blocks the item's damage
    [InlineData("sheerforce")] // Sheer Force removed the move's secondary effects
    [InlineData("status")] // status moves never cost HP
    [InlineData("ohko")] // Gen 6: no recoil after a one-hit KO move
    public void Life_orb_costs_no_hp_without_a_damaging_hit_or_under_magic_guard_and_sheer_force(string scenario)
    {
        var (move, ability, rolls) = NoRecoilScenario(scenario);
        var user = Holding(Mon("User", 100, moves: [move]), "lifeorb") with { Ability = ability };
        var battle = Singles([user], [Mon("Target", 50, moves: [Splash])], new PercentRolls(rolls));
        battle.Start();

        var events = Turn(battle);
        Assert.Empty(events.OfType<ItemDamage>());
        Assert.Equal(400, battle.GetSide(SideId.P1).Active[0]!.CurrentHp);
    }

    private static (MoveDefinition Move, string? Ability, int[] Rolls) NoRecoilScenario(string scenario) => scenario switch
    {
        "miss" => (new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50, Accuracy: 50),
            null, [99]),
        "magicguard" => (Tackle, "magicguard", []),
        "sheerforce" => (new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50,
            Secondaries: [new SecondaryEffect(10, Status: MajorStatus.Paralysis)]), "sheerforce", []),
        "status" => (Splash, null, []),
        _ => (new MoveDefinition("guillotine", "Guillotine", PokemonType.Normal, MoveCategory.Physical, 0,
            Accuracy: 30, Ohko: true), null, [0]),
    };

    [Theory]
    [InlineData(null, 60, "Foe")]
    [InlineData("choicescarf", 60, "User")] // 90 > 80
    [InlineData("ironball", 100, "Foe")] // 50 < 80
    [InlineData("machobrace", 100, "Foe")]
    [InlineData("powerweight", 100, "Foe")]
    [InlineData("powerbracer", 100, "Foe")]
    [InlineData("powerbelt", 100, "Foe")]
    [InlineData("powerlens", 100, "Foe")]
    [InlineData("powerband", 100, "Foe")]
    [InlineData("poweranklet", 100, "Foe")]
    [InlineData("leftovers", 100, "User")] // no speed effect
    public void Speed_items_change_turn_order(string? item, int speed, string firstMover)
    {
        var user = Mon("User", speed) with { Item = item };
        var battle = Singles([user], [Mon("Foe", 80)]);
        battle.Start();

        Assert.Equal(firstMover, FirstMover(Turn(battle)));
        Assert.Equal(item switch
        {
            "choicescarf" => 90,
            null or "leftovers" => speed,
            _ => speed / 2,
        }, battle.GetSide(SideId.P1).Active[0]!.Speed);
    }

    [Theory]
    [InlineData("Ditto", 80)]
    [InlineData("Mon", 40)]
    public void Quick_powder_doubles_dittos_speed(string species, int expectedSpeed)
    {
        var battle = Singles([Mon(species, 40) with { Item = "quickpowder" }], [Mon("Foe", 60)]);
        battle.Start();

        Assert.Equal(expectedSpeed, battle.GetSide(SideId.P1).Active[0]!.Speed);
    }

    [Theory]
    [InlineData(0, "User")] // 1 in 5: moves first within its priority bracket
    [InlineData(1, "Foe")]
    public void Quick_claw_sometimes_moves_first(int roll, string firstMover)
    {
        var battle = Singles(
            [Mon("User", 50) with { Item = "quickclaw" }], [Mon("Foe", 100)], new PercentRolls().Script(0, 5, roll));
        battle.Start();

        var events = Turn(battle);
        Assert.Equal(firstMover, FirstMover(events));
        Assert.Equal(roll == 0 ? 1 : 0, events.OfType<ItemActivated>().Count(e => e is { Item: "quickclaw", Side: SideId.P1 }));
    }

    [Fact]
    public void Quick_claw_does_not_beat_a_higher_priority_move()
    {
        var battle = Singles(
            [Mon("User", 50) with { Item = "quickclaw" }],
            [Mon("Foe", 10, moves: [QuickAttack])],
            new PercentRolls().Script(0, 5, 0));
        battle.Start();

        Assert.Equal("Foe", FirstMover(Turn(battle)));
    }

    [Theory]
    [InlineData("laggingtail")]
    [InlineData("fullincense")]
    public void Lagging_tail_and_full_incense_move_last_in_their_bracket(string item)
    {
        var battle = Singles([Mon("User", 100) with { Item = item }], [Mon("Foe", 10)]);
        battle.Start();

        Assert.Equal("Foe", FirstMover(Turn(battle)));
    }

    [Theory]
    [InlineData(null, 79, true)]
    [InlineData(null, 80, false)]
    [InlineData("widelens", 87, true)] // 80 x 4505/4096 = 88
    [InlineData("widelens", 88, false)]
    public void Wide_lens_raises_accuracy(string? item, int roll, bool hits)
    {
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50, Accuracy: 80);
        var battle = Singles(
            [Mon("User", 100, moves: [move]) with { Item = item }], [Mon("Foe", 10, moves: [Splash])], new PercentRolls(roll));
        battle.Start();

        Assert.Equal(hits, Turn(battle).OfType<DamageDealt>().Any(d => d.Side == SideId.P2));
    }

    [Theory]
    [InlineData(10, true)] // the target already moved: 80 x 4915/4096 = 96
    [InlineData(100, false)] // the user moves first: 80
    public void Zoom_lens_raises_accuracy_when_moving_after_the_target(int userSpeed, bool hits)
    {
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50, Accuracy: 80);
        var battle = Singles(
            [Mon("User", userSpeed, moves: [move]) with { Item = "zoomlens" }],
            [Mon("Foe", 50, moves: [Splash])],
            new PercentRolls(90));
        battle.Start();

        Assert.Equal(hits, Turn(battle).OfType<DamageDealt>().Any(d => d.Side == SideId.P2));
    }

    [Theory]
    [InlineData("brightpowder", 89, true)] // 100 x 3686/4096 = 90
    [InlineData("brightpowder", 90, false)]
    [InlineData("laxincense", 90, false)]
    [InlineData(null, 99, true)]
    public void Bright_powder_and_lax_incense_lower_the_accuracy_of_moves_against_the_holder(string? item, int roll, bool hits)
    {
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50, Accuracy: 100);
        var battle = Singles(
            [Mon("User", 100, moves: [move])],
            [Mon("Foe", 10, moves: [Splash]) with { Item = item }],
            new PercentRolls(roll));
        battle.Start();

        Assert.Equal(hits, Turn(battle).OfType<DamageDealt>().Any(d => d.Side == SideId.P2));
    }

    [Fact]
    public void Wide_lens_and_bright_powder_chain_after_abilities()
    {
        // ModifyAccuracy: Compound Eyes (priority -1) x1.3, then the items (-2): Wide Lens x1.1 and
        // Bright Powder x0.9 -> 5325 x 4505 x 3686 = 5270 or 5271/4096 depending on the order;
        // 70 x 5271/4096 = 90 either way (70 x 5270/4096 = 90.06).
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50, Accuracy: 70);
        var battle = Singles(
            [WithAbility(Mon("User", 100, moves: [move]), "compoundeyes") with { Item = "widelens" }],
            [Mon("Foe", 10, moves: [Splash]) with { Item = "brightpowder" }],
            new PercentRolls(89, 90));
        battle.Start();

        Assert.Contains(Turn(battle), e => e is DamageDealt { Side: SideId.P2 });
        Assert.DoesNotContain(Turn(battle), e => e is DamageDealt { Side: SideId.P2 });
    }

    [Fact]
    public void Iron_ball_grounds_flying_and_levitate_holders()
    {
        var chart = new TestTypeChart(new() { [(PokemonType.Ground, PokemonType.Flying)] = 0 });
        var quake = new MoveDefinition("earthquake", "Earthquake", PokemonType.Ground, MoveCategory.Physical, 50);
        var flyer = Mon("Flyer", 10, moves: [Splash]) with { Types = [PokemonType.Flying], Item = "ironball" };
        var levitating = WithAbility(Mon("Floater", 10, moves: [Splash]), "levitate") with { Item = "ironball" };
        var battle = Battle.Create(
            BattleFormat.Singles, [Mon("User", 100, moves: [quake])], [flyer, levitating], chart, new MaxRandom());
        battle.Start();

        // Neutral damage against the Flying type: 24 (no STAB).
        Assert.Equal(24, DamageTo(Turn(battle), SideId.P2));
        battle.Submit(SideId.P1, Move());
        var events = battle.Submit(SideId.P2, Switch(0, 1)).Events;
        Assert.Empty(events.OfType<AbilityImmune>());
        Assert.Contains(events, e => e is DamageDealt { Side: SideId.P2, Species: "Floater" });
    }

    [Fact]
    public void Arena_trap_traps_a_flying_pokemon_holding_an_iron_ball()
    {
        var target = Mon("Target", 100, moves: [Splash]) with { Types = [PokemonType.Flying], Item = "ironball" };
        var battle = Singles([target, Mon("Bench", 10)], [WithAbility(Mon("Trapper", 50, moves: [Splash]), "arenatrap")]);
        battle.Start();

        Assert.Equal([0], battle.PendingRequests.Single(r => r.Side == SideId.P1).TrappedSlots);
    }

    [Fact]
    public void Metronome_boosts_consecutive_uses_of_the_same_move()
    {
        var other = new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50);
        var user = Holding(Mon("User", 100, moves: [Tackle, other]), "metronome");
        var battle = Singles([user], [Mon("Target", 50, hp: 1000, moves: [Splash])]);
        battle.Start();

        // 36, then x4915/4096 = 43, then x5734/4096 = 50; a different move starts again at 36.
        Assert.Equal(36, DamageTo(Turn(battle), SideId.P2));
        Assert.Equal(43, DamageTo(Turn(battle), SideId.P2));
        Assert.Equal(50, DamageTo(Turn(battle), SideId.P2));
        Assert.Equal(36, DamageTo(Turn(battle, p1Move: 1), SideId.P2));
    }

    [Fact]
    public void Metronome_restarts_after_a_move_that_failed()
    {
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50, Accuracy: 90);
        var user = Holding(Mon("User", 100, moves: [move]), "metronome");
        var battle = Singles([user], [Mon("Target", 50, hp: 1000, moves: [Splash])], new PercentRolls(0, 99, 0));
        battle.Start();

        Assert.Equal(36, DamageTo(Turn(battle), SideId.P2));
        Assert.Empty(Turn(battle).OfType<DamageDealt>()); // second use (x1.2) misses
        Assert.Equal(36, DamageTo(Turn(battle), SideId.P2)); // last turn's use failed: no boost
    }

    [Fact]
    public void Metronome_counts_the_attack_turn_of_a_two_turn_move()
    {
        // Showdown's metronome onTryMove: the charging turn is not counted, and the attacking turn of
        // a new two-turn move counts as the first consecutive use (x1.2): 24 x 4915/4096 = 29.
        var charge = new MoveDefinition("razorwind", "Razor Wind", PokemonType.Fire, MoveCategory.Special, 50, Charge: true);
        var user = Holding(Mon("User", 100, moves: [charge]), "metronome");
        var battle = Singles([user], [Mon("Target", 50, hp: 1000, moves: [Splash])]);
        battle.Start();

        Assert.Empty(Turn(battle).OfType<DamageDealt>());
        // The user is locked into its attack, so only the foe is asked for a decision.
        Assert.Equal(29, DamageTo(battle.Submit(SideId.P2, Move()).Events, SideId.P2));
    }
}
