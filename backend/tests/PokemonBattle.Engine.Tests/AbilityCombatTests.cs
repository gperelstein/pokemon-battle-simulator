using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Abilities triggered by hits, contact, status, stat drops and KOs, checked against Showdown's
/// data/abilities.ts with the Gen 6 mods (Aftermath, Weak Armor from gen6; Rattled from gen7) and
/// the hit order of sim/battle-actions.ts (damage, move effects, secondaries, then DamagingHit).
/// </summary>
public class AbilityCombatTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition Punch = new(
        "megapunch", "Mega Punch", PokemonType.Normal, MoveCategory.Physical, 50, Flags: MoveFlags.Contact | MoveFlags.Punch);

    private static readonly MoveDefinition Swift = new("swift", "Swift", PokemonType.Normal, MoveCategory.Special, 50);

    private static readonly MoveDefinition ThunderWave = new(
        "thunderwave", "Thunder Wave", PokemonType.Electric, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Status: MajorStatus.Paralysis, IgnoreImmunity: false);

    private static readonly MoveDefinition Spore = new(
        "spore", "Spore", PokemonType.Grass, MoveCategory.Status, 0, Target: MoveTarget.Normal, Status: MajorStatus.Sleep);

    private static readonly MoveDefinition Growl = new(
        "growl", "Growl", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.AllAdjacentFoes,
        Boosts: [Stage(BoostStat.Attack, -1)]);

    private static readonly MoveDefinition Crunch = new("crunch", "Crunch", PokemonType.Dark, MoveCategory.Physical, 50);

    private static readonly MoveDefinition BugBite = new("bugbite", "Bug Bite", PokemonType.Bug, MoveCategory.Physical, 50);

    private static readonly MoveDefinition StormThrow = new(
        "stormthrow", "Storm Throw", PokemonType.Fighting, MoveCategory.Physical, 60, WillCrit: true);

    private static readonly MoveDefinition Spark = new(
        "spark", "Spark", PokemonType.Electric, MoveCategory.Physical, 50,
        Secondaries: [new SecondaryEffect(30, Status: MajorStatus.Paralysis)]);

    private static PokemonSet With(string species, int speed, string ability, params MoveDefinition[] moves) =>
        WithAbility(Mon(species, speed, moves: moves.Length == 0 ? [Splash] : moves), ability);

    private static IReadOnlyList<string> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return Describe(battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events);
    }

    private static BattlePokemon Active(Battle battle, SideId side) => battle.GetSide(side).Active[0]!;

    [Theory]
    [InlineData("static", MajorStatus.Paralysis, 2, true)] // 3 in 10
    [InlineData("static", MajorStatus.Paralysis, 3, false)]
    [InlineData("flamebody", MajorStatus.Burn, 0, true)]
    [InlineData("poisonpoint", MajorStatus.Poison, 0, true)]
    public void Contact_abilities_may_inflict_a_status_on_the_attacker(string ability, MajorStatus status, int roll, bool inflicted)
    {
        var battle = Singles(
            [Mon("User", 100, moves: [Punch])], [With("Foe", 50, ability)], new PercentRolls().Script(0, 10, roll));
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(inflicted, events.Contains($"P2.0 Foe {ability} activates") && events.Contains($"P1.0 User gets {status}"));
        Assert.Equal(inflicted ? status : null, Active(battle, SideId.P1).Status);
    }

    [Fact]
    public void Contact_abilities_ignore_moves_without_contact()
    {
        // No 1-in-10 roll is scripted: none may happen.
        var battle = Singles([Mon("User", 100, moves: [Swift])], [With("Foe", 50, "static")], new PercentRolls().Script(0, 10));
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("static"));
    }

    [Fact]
    public void Contact_abilities_still_work_when_their_holder_faints()
    {
        var battle = Singles(
            [Mon("User", 100, moves: [Punch])], [With("Foe", 50, "static") with { Stats = new Stats(10, 100, 100, 100, 100, 50) }, Mon("Spare", 10)],
            new PercentRolls().Script(0, 10, 0));
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Foe fainted", events);
        Assert.Contains("P1.0 User gets Paralysis", events);
    }

    [Theory]
    [InlineData(10, "Sleep")]
    [InlineData(20, "Paralysis")]
    [InlineData(29, "Poison")]
    [InlineData(30, null)]
    public void Effect_spore_may_inflict_sleep_paralysis_or_poison(int roll, string? status)
    {
        var battle = Singles([Mon("User", 100, moves: [Punch])], [With("Paras", 50, "effectspore")], new PercentRolls(roll));
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(status is not null, events.Any(e => e.StartsWith("P1.0 User gets")));
        if (status is not null)
        {
            Assert.Contains($"P1.0 User gets {status}", events);
        }
    }

    [Fact]
    public void Effect_spore_does_not_affect_grass_types()
    {
        var battle = Singles(
            [Mon("User", 100, moves: [Punch]) with { Types = [PokemonType.Grass] }], [With("Paras", 50, "effectspore")],
            new PercentRolls());
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("gets"));
    }

    [Fact]
    public void Poison_touch_may_poison_the_target_of_a_contact_move()
    {
        var battle = Singles([With("Grimer", 100, "poisontouch", Punch)], [Mon("Foe", 50, moves: [Splash])],
            new PercentRolls().Script(0, 10, 0));
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P2.0 Foe damaged", "P1.0 Grimer poisontouch activates", "P2.0 Foe gets Poison"], events.Skip(1).Take(3));
    }

    [Fact]
    public void Aftermath_hurts_the_attacker_that_knocks_out_its_holder_with_contact()
    {
        var battle = Singles(
            [Mon("User", 100, moves: [Punch, Swift])],
            [With("Voltorb", 50, "aftermath") with { Stats = new Stats(10, 100, 100, 100, 100, 50) }, With("Electrode", 40, "aftermath") with { Stats = new Stats(10, 100, 100, 100, 100, 50) }]);
        battle.Start();

        var contact = Turn(battle);
        battle.Submit(SideId.P2, Switch(0, 1));
        var special = Turn(battle, p1Move: 1);

        Assert.Equal(["P2.0 Voltorb aftermath activates", "P1.0 User hurt by aftermath 100"],
            contact.SkipWhile(e => e != "P2.0 Voltorb fainted").Skip(1).Take(2));
        Assert.DoesNotContain(special, e => e.Contains("aftermath"));
    }

    [Fact]
    public void Synchronize_passes_burn_paralysis_and_poison_back_but_not_sleep()
    {
        var paralysis = Singles([Mon("User", 100, moves: [ThunderWave])], [With("Abra", 50, "synchronize")]);
        paralysis.Start();
        var sleep = Singles([Mon("User", 100, moves: [Spore])], [With("Abra", 50, "synchronize")]);
        sleep.Start();

        var paralyzed = Turn(paralysis);
        var slept = Turn(sleep);

        Assert.Equal(["P2.0 Abra gets Paralysis", "P2.0 Abra synchronize activates", "P1.0 User gets Paralysis"],
            paralyzed.Skip(1).Take(3));
        Assert.DoesNotContain(slept, e => e.Contains("synchronize"));
    }

    [Fact]
    public void Anger_point_maximizes_attack_after_a_critical_hit()
    {
        var battle = Singles([Mon("User", 100, moves: [StormThrow])], [With("Primeape", 50, "angerpoint")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Primeape angerpoint activates", events);
        Assert.Contains("P2.0 Primeape Attack +6 = 6", events);
    }

    [Theory]
    [InlineData("defiant", "Attack")]
    [InlineData("competitive", "SpecialAttack")]
    public void Defiant_and_competitive_answer_a_stat_drop_with_plus_two(string ability, string stat)
    {
        var battle = Singles([Mon("User", 100, moves: [Growl])], [With("Primeape", 50, ability)]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(
            ["P2.0 Primeape Attack -1 = -1", $"P2.0 Primeape {ability} activates", $"P2.0 Primeape {stat} +2 = {(stat == "Attack" ? 1 : 2)}"],
            events.Skip(1).Take(3));
    }

    [Fact]
    public void Defiant_also_answers_intimidate()
    {
        var battle = Singles([With("Gyarados", 81, "intimidate")], [With("Primeape", 50, "defiant")]);

        Assert.Contains("P2.0 Primeape Attack +2 = 1", Describe(battle.Start()));
    }

    [Theory]
    [InlineData("justified", "crunch", "P2.0 Foe Attack +1 = 1")]
    [InlineData("rattled", "bugbite", "P2.0 Foe Speed +1 = 1")]
    [InlineData("rattled", "crunch", "P2.0 Foe Speed +1 = 1")]
    public void Some_abilities_react_to_the_type_of_the_move_that_hit(string ability, string moveId, string expected)
    {
        var move = new[] { Crunch, BugBite }.Single(m => m.Id == moveId);
        var battle = Singles([Mon("User", 100, moves: [move])], [With("Foe", 50, ability)]);
        battle.Start();

        Assert.Contains(expected, Turn(battle));
    }

    [Fact]
    public void Rattled_ignores_intimidate_in_gen_6()
    {
        var battle = Singles([With("Gyarados", 81, "intimidate")], [With("Foe", 50, "rattled")]);

        Assert.DoesNotContain(Describe(battle.Start()), e => e.Contains("Speed"));
    }

    [Fact]
    public void Weak_armor_trades_defense_for_speed_on_physical_hits()
    {
        var battle = Singles([Mon("User", 100, moves: [Punch])], [With("Onix", 50, "weakarmor")]);
        battle.Start();

        var events = Turn(battle);

        // Gen 6: Defense -1 and Speed +1 (Speed +2 from Gen 7).
        Assert.Contains("P2.0 Onix Defense -1 = -1", events);
        Assert.Contains("P2.0 Onix Speed +1 = 1", events);
    }

    [Fact]
    public void Moxie_raises_attack_after_a_knock_out()
    {
        var battle = Singles(
            [With("Gyarados", 100, "moxie", Punch)],
            [Mon("Foe", 50, moves: [Splash]) with { Stats = new Stats(10, 100, 100, 100, 100, 50) }, Mon("Spare", 10)]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P2.0 Foe fainted", "P1.0 Gyarados moxie activates", "P1.0 Gyarados Attack +1 = 1"],
            events.SkipWhile(e => e != "P2.0 Foe fainted").Take(3));
    }

    [Theory]
    [InlineData(59, true)] // 30% doubled to 60%
    [InlineData(60, false)]
    public void Serene_grace_doubles_secondary_chances(int roll, bool paralyzed)
    {
        var battle = Singles([With("Chansey", 100, "serenegrace", Spark)], [Mon("Foe", 50, moves: [Splash])], new PercentRolls(roll));
        battle.Start();

        Assert.Equal(paralyzed, Turn(battle).Contains("P2.0 Foe gets Paralysis"));
    }

    [Fact]
    public void Sheer_force_drops_secondary_effects_for_more_power()
    {
        var user = With("Nidoking", 100, "sheerforce", Spark, Spark with { Secondaries = [], HasSecondaryEffects = true });
        var foe = Mon("Foe", 50, moves: [Splash]);
        var battle = Singles([user], [foe], new PercentRolls());
        battle.Start();

        battle.Submit(SideId.P1, Move());
        var spark = battle.Submit(SideId.P2, Move()).Events;
        battle.Submit(SideId.P1, Move(moveIndex: 1));
        var flinchOnly = battle.Submit(SideId.P2, Move()).Events;

        // x1.3 (5325/4096): 50 power becomes 65; no secondary roll happens.
        var expected = new DamageCalculator(TestTypeChart.Gen6Sample)
            .Calculate(user, foe, Spark with { Id = "fixed", BasePower = 65, Secondaries = [] }, false, new MaxRandom()).Damage;
        Assert.Equal(expected, spark.OfType<DamageDealt>().Single(d => d.Side == SideId.P2).Amount);
        Assert.Equal(expected, flinchOnly.OfType<DamageDealt>().Single(d => d.Side == SideId.P2).Amount);
        Assert.Null(Active(battle, SideId.P2).Status);
    }
}
