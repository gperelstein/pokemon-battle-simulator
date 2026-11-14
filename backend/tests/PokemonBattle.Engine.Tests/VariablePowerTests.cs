using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Moves whose power or damage is computed by move-specific logic, checked against the
/// basePowerCallback / damageCallback code of Showdown's data/moves.ts (Gen 6 inherits it).
/// Each test compares the damage dealt with the formula fed the expected base power.
/// </summary>
public class VariablePowerTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static MoveDefinition Physical(string id, PokemonType type, int basePower = 0, int priority = 0) =>
        new(id, id, type, MoveCategory.Physical, basePower, priority, MoveTarget.Normal);

    private static MoveDefinition Special(string id, PokemonType type, int basePower = 0, int priority = 0) =>
        new(id, id, type, MoveCategory.Special, basePower, priority, MoveTarget.Normal);

    /// <summary>Deals exactly <paramref name="amount"/> before anything else this turn.</summary>
    private static MoveDefinition Damager(int amount) =>
        new("dragonrage", "Dragon Rage", PokemonType.Dragon, MoveCategory.Special, 0, Priority: 1, FixedDamage: amount);

    private static MoveDefinition Inflict(MajorStatus status) =>
        new("inflict", "Inflict", PokemonType.Normal, MoveCategory.Status, 0, Priority: 1, Target: MoveTarget.Normal,
            Status: status);

    private static MoveDefinition SelfBoost(params StatChange[] boosts) =>
        new("boost", "Boost", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self, Boosts: boosts);

    private static int ExpectedDamage(MoveDefinition move, int basePower, PokemonSet user, PokemonSet target) =>
        new DamageCalculator(TestTypeChart.Gen6Sample)
            .Calculate(user, target, move with { Id = "fixed", BasePower = basePower }, false, new MaxRandom())
            .Damage;

    private static IReadOnlyList<BattleEvent> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events;
    }

    private static int DamageTo(IEnumerable<BattleEvent> events, SideId side) =>
        events.OfType<DamageDealt>().Last(d => d.Side == side).Amount;

    /// <summary>One turn where P1 uses its first move against a P2 that only splashes.</summary>
    private static IReadOnlyList<BattleEvent> HitOnce(PokemonSet user, PokemonSet target, IBattleRandom? random = null)
    {
        var battle = Singles([user], [target with { Moves = [Splash] }], random ?? new MaxRandom());
        battle.Start();
        return Turn(battle);
    }

    [Theory]
    [InlineData(99, 20)]
    [InlineData(100, 40)]
    [InlineData(249, 40)]
    [InlineData(250, 60)]
    [InlineData(500, 80)]
    [InlineData(1000, 100)]
    [InlineData(2000, 120)]
    public void Low_kick_and_grass_knot_grow_with_the_target_weight(int targetWeightHg, int expectedPower)
    {
        foreach (var move in new[] { Physical("lowkick", PokemonType.Fighting), Special("grassknot", PokemonType.Grass) })
        {
            var user = Mon("User", 100, moves: [move]);
            var target = Mon("Target", 50) with { WeightHg = targetWeightHg };

            Assert.Equal(ExpectedDamage(move, expectedPower, user, target), DamageTo(HitOnce(user, target), SideId.P2));
        }
    }

    [Theory]
    [InlineData(500, 100, 120)]
    [InlineData(499, 100, 100)]
    [InlineData(300, 100, 80)]
    [InlineData(200, 100, 60)]
    [InlineData(199, 100, 40)]
    public void Heavy_slam_and_heat_crash_compare_both_weights(int userWeightHg, int targetWeightHg, int expectedPower)
    {
        foreach (var move in new[] { Physical("heavyslam", PokemonType.Steel), Physical("heatcrash", PokemonType.Fire) })
        {
            var user = Mon("User", 100, moves: [move]) with { WeightHg = userWeightHg };
            var target = Mon("Target", 50) with { WeightHg = targetWeightHg };

            Assert.Equal(ExpectedDamage(move, expectedPower, user, target), DamageTo(HitOnce(user, target), SideId.P2));
        }
    }

    [Theory]
    [InlineData(1, 200)]
    [InlineData(4, 200)]
    [InlineData(5, 150)]
    [InlineData(11, 100)]
    [InlineData(21, 80)]
    [InlineData(36, 40)]
    [InlineData(69, 20)]
    public void Flail_and_reversal_grow_as_the_user_loses_hp(int remainingHp, int expectedPower)
    {
        foreach (var move in new[] { Physical("flail", PokemonType.Normal), Physical("reversal", PokemonType.Fighting) })
        {
            var user = Mon("User", 10, hp: 100, moves: [move]);
            var foe = Mon("Foe", 50, moves: [Damager(100 - remainingHp)]);
            var battle = Singles([user], [foe]);
            battle.Start();

            Assert.Equal(ExpectedDamage(move, expectedPower, user, foe), DamageTo(Turn(battle), SideId.P2));
        }
    }

    [Fact]
    public void Eruption_and_water_spout_scale_with_the_user_hp_and_floor_the_power()
    {
        foreach (var move in new[] { Special("eruption", PokemonType.Fire, 150), Special("waterspout", PokemonType.Water, 150) })
        {
            var user = Mon("User", 10, hp: 301, moves: [move]);
            var foe = Mon("Foe", 50, moves: [Damager(101)]);
            var battle = Singles([user], [foe]);
            battle.Start();

            // 150 * 200 / 301 = 99.67, floored to 99.
            Assert.Equal(ExpectedDamage(move, 99, user, foe), DamageTo(Turn(battle), SideId.P2));
        }
    }

    [Theory]
    [InlineData(150, 60)]
    [InlineData(299, 119)]
    [InlineData(5, 1)] // floor(5 * 4096 / 300) = 68: 120 * 6800 / 4096 / 100 = 1.99, floored to 1
    public void Wring_out_and_crush_grip_shrink_with_the_target_hp(int remainingHp, int expectedPower)
    {
        foreach (var move in new[] { Special("wringout", PokemonType.Normal), Physical("crushgrip", PokemonType.Normal) })
        {
            var user = Mon("User", 100, moves: [move, Damager(300 - remainingHp)]);
            var target = Mon("Target", 50, hp: 300, moves: [Splash]);
            var battle = Singles([user], [target]);
            battle.Start();
            Turn(battle, p1Move: 1);

            Assert.Equal(ExpectedDamage(move, expectedPower, user, target), DamageTo(Turn(battle), SideId.P2));
        }
    }

    [Theory]
    [InlineData(50, 100, 51)]
    [InlineData(100, 100, 26)]
    [InlineData(10, 200, 150)]
    public void Gyro_ball_favours_slower_users(int userSpeed, int targetSpeed, int expectedPower)
    {
        var move = Physical("gyroball", PokemonType.Steel);
        var user = Mon("User", userSpeed, moves: [move]);
        var target = Mon("Target", targetSpeed);

        Assert.Equal(ExpectedDamage(move, expectedPower, user, target), DamageTo(HitOnce(user, target), SideId.P2));
    }

    [Theory]
    [InlineData(400, 100, 150)]
    [InlineData(300, 100, 120)]
    [InlineData(200, 100, 80)]
    [InlineData(100, 100, 60)]
    [InlineData(99, 100, 40)]
    public void Electro_ball_favours_faster_users(int userSpeed, int targetSpeed, int expectedPower)
    {
        var move = Special("electroball", PokemonType.Electric);
        var user = Mon("User", userSpeed, moves: [move]);
        var target = Mon("Target", targetSpeed);

        Assert.Equal(ExpectedDamage(move, expectedPower, user, target), DamageTo(HitOnce(user, target), SideId.P2));
    }

    [Theory]
    [InlineData(MajorStatus.Burn)]
    [InlineData(MajorStatus.Paralysis)]
    [InlineData(MajorStatus.Poison)]
    public void Facade_doubles_when_the_user_has_a_status_and_ignores_burn(MajorStatus status)
    {
        var facade = Physical("facade", PokemonType.Normal, 70);
        var user = Mon("User", 100, moves: [facade]);
        var foe = Mon("Foe", 50, moves: [Inflict(status)]);
        var battle = Singles([user], [foe]);
        battle.Start();

        Assert.Equal(ExpectedDamage(facade, 140, user, foe), DamageTo(Turn(battle), SideId.P2));
    }

    [Theory]
    [InlineData("hex", 65, MajorStatus.Paralysis, true)]
    [InlineData("hex", 65, null, false)]
    [InlineData("venoshock", 65, MajorStatus.Poison, true)]
    [InlineData("venoshock", 65, MajorStatus.Toxic, true)]
    [InlineData("venoshock", 65, MajorStatus.Burn, false)]
    [InlineData("smellingsalts", 70, MajorStatus.Paralysis, true)]
    [InlineData("smellingsalts", 70, MajorStatus.Burn, false)]
    [InlineData("wakeupslap", 70, MajorStatus.Sleep, true)]
    [InlineData("wakeupslap", 70, MajorStatus.Paralysis, false)]
    public void Some_moves_double_against_a_target_with_a_status(string id, int basePower, MajorStatus? status, bool doubled)
    {
        var move = Physical(id, PokemonType.Normal, basePower);
        var setup = status is { } s ? Inflict(s) : Splash;
        var user = Mon("User", 100, moves: [move, setup]);
        var target = Mon("Target", 50, moves: [Splash]);
        var battle = Singles([user], [target]);
        battle.Start();
        Turn(battle, p1Move: 1);

        var expected = ExpectedDamage(move, doubled ? basePower * 2 : basePower, user, target);
        Assert.Equal(expected, DamageTo(Turn(battle), SideId.P2));
    }

    [Theory]
    [InlineData("smellingsalts", MajorStatus.Paralysis)]
    [InlineData("wakeupslap", MajorStatus.Sleep)]
    public void Smelling_salts_and_wake_up_slap_cure_the_status_they_exploit(string id, MajorStatus status)
    {
        var battle = Singles(
            [Mon("User", 100, moves: [Physical(id, PokemonType.Normal, 70), Inflict(status)])],
            [Mon("Target", 50, moves: [Splash])]);
        battle.Start();
        Turn(battle, p1Move: 1);

        var events = Describe(Turn(battle));

        Assert.Equal([$"P1.0 User uses {id}", "P2.0 Target damaged", $"P2.0 Target cured of {status}"], events.Take(3));
        Assert.Null(battle.GetSide(SideId.P2).Active[0]!.Status);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(199, false)]
    public void Brine_doubles_against_a_target_at_half_hp_or_less(int damageFirst, bool doubled)
    {
        var brine = Special("brine", PokemonType.Water, 65);
        var user = Mon("User", 100, moves: [brine, Damager(damageFirst)]);
        var target = Mon("Target", 50, moves: [Splash]);
        var battle = Singles([user], [target]);
        battle.Start();
        Turn(battle, p1Move: 1);

        Assert.Equal(ExpectedDamage(brine, doubled ? 130 : 65, user, target), DamageTo(Turn(battle), SideId.P2));
    }

    [Fact]
    public void Stored_power_adds_twenty_per_positive_stage_of_the_user()
    {
        var storedPower = Special("storedpower", PokemonType.Psychic, 20);
        var boost = SelfBoost(Stage(BoostStat.Attack, 2), Stage(BoostStat.Speed, 1), Stage(BoostStat.Defense, -1));
        var user = Mon("User", 100, moves: [storedPower, boost]);
        var target = Mon("Target", 50, moves: [Splash]);
        var battle = Singles([user], [target]);
        battle.Start();
        Turn(battle, p1Move: 1);

        // Three positive stages (the Defense drop does not count): 20 + 3 * 20 = 80.
        Assert.Equal(ExpectedDamage(storedPower, 80, user, target), DamageTo(Turn(battle), SideId.P2));
    }

    [Fact]
    public void Punishment_adds_twenty_per_positive_stage_of_the_target()
    {
        var punishment = Physical("punishment", PokemonType.Dark);
        var user = Mon("User", 100, moves: [punishment, Splash]);
        var target = Mon("Target", 50, moves: [SelfBoost(Stage(BoostStat.Attack, 2), Stage(BoostStat.Speed, 1)), Splash]);
        var battle = Singles([user], [target]);
        battle.Start();
        Turn(battle, p1Move: 1);

        var events = Turn(battle, p2Move: 1);

        Assert.Equal(ExpectedDamage(punishment, 120, user, target), DamageTo(events, SideId.P2));
    }

    [Theory]
    [InlineData(255, 102, 1)]
    [InlineData(0, 1, 102)]
    [InlineData(128, 51, 50)]
    public void Return_and_frustration_depend_on_happiness(int happiness, int returnPower, int frustrationPower)
    {
        var returnMove = Physical("return", PokemonType.Normal);
        var frustration = Physical("frustration", PokemonType.Normal);
        var target = Mon("Target", 50);
        var returnUser = Mon("User", 100, moves: [returnMove]) with { Happiness = happiness };
        var frustrationUser = Mon("User", 100, moves: [frustration]) with { Happiness = happiness };

        Assert.Equal(ExpectedDamage(returnMove, returnPower, returnUser, target), DamageTo(HitOnce(returnUser, target), SideId.P2));
        Assert.Equal(
            ExpectedDamage(frustration, frustrationPower, frustrationUser, target),
            DamageTo(HitOnce(frustrationUser, target), SideId.P2));
    }

    [Theory]
    [InlineData(4, 4, 10)]
    [InlineData(5, 5, 30)]
    [InlineData(34, 6, 50)]
    [InlineData(35, 7, 70)]
    [InlineData(84, 8, 90)]
    [InlineData(85, 9, 110)]
    [InlineData(95, 10, 150)]
    public void Magnitude_rolls_its_power(int roll, int magnitude, int expectedPower)
    {
        var move = new MoveDefinition(
            "magnitude", "Magnitude", PokemonType.Ground, MoveCategory.Physical, 0, Target: MoveTarget.AllAdjacent, Accuracy: 100);
        var user = Mon("User", 100, moves: [move]);
        var target = Mon("Target", 50);

        var events = HitOnce(user, target, new PercentRolls(roll));

        Assert.Equal(new MagnitudeActivated(SideId.P1, 0, magnitude), events[1]);
        Assert.Equal(ExpectedDamage(move, expectedPower, user, target), DamageTo(events, SideId.P2));
    }

    [Theory]
    [InlineData(2, 40)]
    [InlineData(5, 40)]
    [InlineData(6, 80)]
    [InlineData(8, 80)]
    [InlineData(9, 120)]
    public void Present_rolls_its_power(int roll, int expectedPower)
    {
        var present = Physical("present", PokemonType.Normal);
        var user = Mon("User", 100, moves: [present]);
        var target = Mon("Target", 50);

        var events = HitOnce(user, target, new PercentRolls().Script(0, 10, roll));

        Assert.Equal(ExpectedDamage(present, expectedPower, user, target), DamageTo(events, SideId.P2));
    }

    [Fact]
    public void Present_sometimes_heals_a_quarter_of_the_target_max_hp()
    {
        var present = Physical("present", PokemonType.Normal);
        var battle = Singles(
            [Mon("User", 100, moves: [present, Damager(200)])],
            [Mon("Target", 50, hp: 401, moves: [Splash])],
            new PercentRolls().Script(0, 10, 0, 1));
        battle.Start();

        var fullHp = Describe(Turn(battle));
        Turn(battle, p1Move: 1);
        var hurt = Turn(battle);

        Assert.Equal(["P1.0 User uses present", "P1.0 present failed"], fullHp.Take(2));
        // Math.round(401 / 4) = 100.
        Assert.Contains(new Healed(SideId.P2, 0, "Target", 100, 301), hurt);
    }

    [Fact]
    public void Super_fang_halves_the_target_current_hp_but_deals_at_least_one()
    {
        var superFang = Physical("superfang", PokemonType.Normal);
        var full = Singles([Mon("User", 100, moves: [superFang])], [Mon("Target", 50, hp: 401, moves: [Splash])]);
        full.Start();
        var oneHp = Singles(
            [Mon("User", 100, moves: [superFang, Damager(400)])], [Mon("Target", 50, hp: 401, moves: [Splash])]);
        oneHp.Start();
        Turn(oneHp, p1Move: 1);

        Assert.Equal(200, DamageTo(Turn(full), SideId.P2)); // floor(401 / 2)
        Assert.Equal(1, DamageTo(Turn(oneHp), SideId.P2));
    }

    [Fact]
    public void Endeavor_brings_the_target_down_to_the_user_hp_and_fails_otherwise()
    {
        var endeavor = Physical("endeavor", PokemonType.Normal);
        var lower = Singles([Mon("User", 100, hp: 100, moves: [endeavor])], [Mon("Target", 50, moves: [Splash])]);
        lower.Start();
        var higher = Singles([Mon("User", 100, hp: 400, moves: [endeavor])], [Mon("Target", 50, hp: 300, moves: [Splash])]);
        higher.Start();

        Assert.Equal(300, DamageTo(Turn(lower), SideId.P2));
        Assert.Equal(["P1.0 User uses endeavor", "P2.0 Target immune"], Describe(Turn(higher)).Take(2));
    }

    [Fact]
    public void Final_gambit_deals_the_user_hp_and_faints_the_user()
    {
        var finalGambit = Special("finalgambit", PokemonType.Fighting) with { SelfDestruct = SelfDestruct.IfHit };
        var battle = Singles(
            [Mon("User", 100, hp: 150, moves: [finalGambit]), Mon("Backup", 10)],
            [Mon("Target", 50, moves: [Splash])]);
        battle.Start();

        var events = Describe(Turn(battle));

        Assert.Equal(["P1.0 User uses finalgambit", "P2.0 Target damaged", "P1.0 User fainted"], events.Take(3));
        Assert.Equal(400 - 150, battle.GetSide(SideId.P2).Active[0]!.CurrentHp);
    }

    [Fact]
    public void Final_gambit_does_not_faint_the_user_when_the_target_is_immune()
    {
        var battle = Singles(
            [Mon("User", 100, hp: 150, moves: [Special("finalgambit", PokemonType.Fighting) with { SelfDestruct = SelfDestruct.IfHit }])],
            [Mon("Gengar", 50, moves: [Splash]) with { Types = [PokemonType.Ghost] }]);
        battle.Start();

        var events = Describe(Turn(battle));

        Assert.Contains("P2.0 Gengar immune", events);
        Assert.False(battle.GetSide(SideId.P1).Active[0]!.IsFainted);
    }

    [Theory]
    [InlineData(50, 150, 75)]
    [InlineData(50, 50, 25)]
    [InlineData(1, 50, 1)] // 0.5 rounds up to the minimum of 1
    public void Psywave_deals_a_random_fraction_of_the_user_level(int level, int roll, int expected)
    {
        var user = Mon("User", 100, moves: [Special("psywave", PokemonType.Psychic)]) with { Level = level };

        var events = HitOnce(user, Mon("Target", 50), new PercentRolls().Script(50, 151, roll));

        Assert.Equal(expected, DamageTo(events, SideId.P2));
    }

    [Theory]
    [InlineData(50, 50, 29, true)]
    [InlineData(50, 50, 30, false)]
    [InlineData(60, 50, 39, true)]
    [InlineData(60, 50, 40, false)]
    public void One_hit_ko_moves_hit_30_percent_plus_the_level_difference(int userLevel, int targetLevel, int roll, bool hits)
    {
        var guillotine = new MoveDefinition(
            "guillotine", "Guillotine", PokemonType.Normal, MoveCategory.Physical, 0, Accuracy: 30, Ohko: true);
        var user = Mon("User", 100, moves: [guillotine]) with { Level = userLevel };
        var target = Mon("Target", 50) with { Level = targetLevel };

        var events = Describe(HitOnce(user, target, new PercentRolls(roll)));

        Assert.Equal(
            hits ? ["P2.0 Target damaged", "P2.0 one-hit KO", "P2.0 Target fainted"] : ["P2.0 Target avoided"],
            events.Skip(1).Take(hits ? 3 : 1));
    }

    [Fact]
    public void One_hit_ko_moves_cannot_affect_a_higher_level_target()
    {
        var sheerCold = new MoveDefinition(
            "sheercold", "Sheer Cold", PokemonType.Ice, MoveCategory.Special, 0, Accuracy: 30, Ohko: true);
        var user = Mon("User", 100, moves: [sheerCold]) with { Level = 49 };

        // No roll is scripted: the level check fails before accuracy.
        var events = Describe(HitOnce(user, Mon("Target", 50), new PercentRolls()));

        Assert.Equal("P2.0 Target immune", events[1]);
    }

    [Fact]
    public void Sheer_cold_can_knock_out_ice_types_in_gen_6()
    {
        var sheerCold = new MoveDefinition(
            "sheercold", "Sheer Cold", PokemonType.Ice, MoveCategory.Special, 0, Accuracy: 30, Ohko: true);

        var events = Describe(HitOnce(
            Mon("User", 100, moves: [sheerCold]), Mon("Jynx", 50) with { Types = [PokemonType.Ice] }, new PercentRolls(0)));

        Assert.Contains("P2.0 one-hit KO", events);
    }

    [Theory]
    [InlineData("counter", MoveCategory.Physical, MoveCategory.Physical, true)]
    [InlineData("counter", MoveCategory.Physical, MoveCategory.Special, false)]
    [InlineData("mirrorcoat", MoveCategory.Special, MoveCategory.Special, true)]
    [InlineData("mirrorcoat", MoveCategory.Special, MoveCategory.Physical, false)]
    public void Counter_and_mirror_coat_return_double_the_damage_of_that_category(
        string id, MoveCategory counterCategory, MoveCategory hitCategory, bool works)
    {
        var counter = new MoveDefinition(id, id, PokemonType.Fighting, counterCategory, 0, Priority: -5, Target: MoveTarget.Scripted);
        var hit = new MoveDefinition("hit", "Hit", PokemonType.Normal, hitCategory, 50);
        var battle = Singles([Mon("User", 100, moves: [counter])], [Mon("Foe", 50, moves: [hit])]);
        battle.Start();

        var events = Turn(battle);

        if (works)
        {
            Assert.Equal(2 * DamageTo(events, SideId.P1), DamageTo(events, SideId.P2));
        }
        else
        {
            Assert.Contains($"P1.0 {id} failed", Describe(events));
        }
    }

    [Fact]
    public void Counter_hits_back_the_foe_that_hit_the_user_whatever_target_was_chosen()
    {
        var counter = new MoveDefinition("counter", "Counter", PokemonType.Fighting, MoveCategory.Physical, 0, Priority: -5,
            Target: MoveTarget.Scripted);
        var battle = Doubles(
            [Mon("User", 100, moves: [counter]), Mon("Ally", 90, moves: [Splash])],
            [Mon("Idle", 50, moves: [Splash]), Mon("Attacker", 40)]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0)));

        var events = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0, 0))).Events;

        Assert.Equal(72, events.OfType<DamageDealt>().Single(d => d.Side == SideId.P2).Amount);
        Assert.Contains(events, e => e is DamageDealt { Side: SideId.P2, Slot: 1 });
    }

    [Fact]
    public void Metal_burst_returns_one_and_a_half_times_the_last_damage()
    {
        var metalBurst = new MoveDefinition("metalburst", "Metal Burst", PokemonType.Steel, MoveCategory.Physical, 0,
            Target: MoveTarget.Scripted);
        var battle = Singles([Mon("User", 10, moves: [metalBurst])], [Mon("Foe", 50)]);
        battle.Start();

        var events = Turn(battle);

        // Tackle deals 36; floor(36 * 1.5) = 54.
        Assert.Equal(54, DamageTo(events, SideId.P2));
    }

    [Fact]
    public void Counter_fails_when_the_user_was_not_hit()
    {
        var counter = new MoveDefinition("counter", "Counter", PokemonType.Fighting, MoveCategory.Physical, 0, Priority: -5,
            Target: MoveTarget.Scripted);
        var battle = Singles([Mon("User", 100, moves: [counter])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Assert.Contains("P1.0 counter failed", Describe(Turn(battle)));
    }

    [Theory]
    [InlineData("revenge", PokemonType.Fighting, true)]
    [InlineData("revenge", PokemonType.Fighting, false)]
    [InlineData("avalanche", PokemonType.Ice, true)]
    public void Revenge_and_avalanche_double_after_being_hit_by_the_target(string id, PokemonType type, bool hitFirst)
    {
        var move = Physical(id, type, 60, priority: -4);
        var user = Mon("User", 100, moves: [move]);
        var foe = Mon("Foe", 50, moves: [hitFirst ? Tackle : Splash]);
        var battle = Singles([user], [foe]);
        battle.Start();

        Assert.Equal(ExpectedDamage(move, hitFirst ? 120 : 60, user, foe), DamageTo(Turn(battle), SideId.P2));
    }

    [Theory]
    [InlineData(10, 100)]
    [InlineData(100, 50)]
    public void Payback_doubles_when_the_target_already_moved(int userSpeed, int expectedPower)
    {
        var payback = Physical("payback", PokemonType.Dark, 50);
        var user = Mon("User", userSpeed, moves: [payback]);
        var foe = Mon("Foe", 50, moves: [Splash]);
        var battle = Singles([user], [foe]);
        battle.Start();

        Assert.Equal(ExpectedDamage(payback, expectedPower, user, foe), DamageTo(Turn(battle), SideId.P2));
    }

    [Fact]
    public void Assurance_doubles_against_a_target_already_hurt_this_turn()
    {
        var assurance = Physical("assurance", PokemonType.Dark, 60);
        var user = Mon("User", 10, moves: [assurance]);
        var target = Mon("Target", 50, moves: [Splash]);
        var battle = Doubles([user, Mon("Ally", 100)], [target, Mon("Other", 20, moves: [Splash])]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0)));

        var events = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0))).Events;

        Assert.Equal(ExpectedDamage(assurance, 120, user, target), DamageTo(events, SideId.P2));
    }
}
