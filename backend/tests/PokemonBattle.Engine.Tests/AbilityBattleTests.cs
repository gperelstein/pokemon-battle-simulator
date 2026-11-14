using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Modifier abilities in battle (base power, accuracy, evasion, speed), checked against Showdown's
/// data/abilities.ts with the Gen 6 mods (Aerilate x1.3, Technician priority 19, Scrappy).
/// </summary>
public class AbilityBattleTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static MoveDefinition Weather(Weather weather) => new(
        weather.ToString().ToLowerInvariant(), weather.ToString(), PokemonType.Normal, MoveCategory.Status, 0,
        Target: MoveTarget.All, SetsWeather: weather);

    private static MoveDefinition Normal(int power, MoveFlags flags = MoveFlags.None, bool recoil = false) =>
        new("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, power, Flags: flags, HasRecoil: recoil);

    private static PokemonSet With(PokemonSet set, string ability) => set with { Ability = ability };

    private static int ExpectedDamage(MoveDefinition move, int basePower, PokemonSet user, PokemonSet target, PokemonType? type = null) =>
        new DamageCalculator(TestTypeChart.Gen6Sample)
            .Calculate(user, target, move with { Id = "fixed", BasePower = basePower, Type = type ?? move.Type }, false, new MaxRandom())
            .Damage;

    private static IReadOnlyList<BattleEvent> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events;
    }

    private static int DamageTo(IEnumerable<BattleEvent> events, SideId side) =>
        events.OfType<DamageDealt>().Last(d => d.Side == side).Amount;

    [Theory]
    [InlineData("technician", 60, MoveFlags.None, false, 90)]
    [InlineData("technician", 61, MoveFlags.None, false, 61)]
    [InlineData("technician", 40, MoveFlags.None, false, 60)]
    [InlineData("toughclaws", 50, MoveFlags.Contact, false, 65)] // 5325/4096
    [InlineData("toughclaws", 50, MoveFlags.None, false, 50)]
    [InlineData("ironfist", 50, MoveFlags.Punch, false, 60)] // 4915/4096
    [InlineData("megalauncher", 50, MoveFlags.Pulse, false, 75)]
    [InlineData("reckless", 50, MoveFlags.None, true, 60)]
    [InlineData("reckless", 50, MoveFlags.None, false, 50)]
    public void Base_power_abilities(string ability, int power, MoveFlags flags, bool recoil, int expectedPower)
    {
        var move = Normal(power, flags, recoil);
        var user = With(Mon("User", 100, moves: [move]), ability);
        var target = Mon("Target", 50, moves: [Splash]);
        var battle = Singles([user], [target]);
        battle.Start();

        Assert.Equal(ExpectedDamage(move, expectedPower, user, target), DamageTo(Turn(battle), SideId.P2));
    }

    [Theory]
    [InlineData(PokemonType.Rock, 65)]
    [InlineData(PokemonType.Normal, 50)]
    public void Sand_force_boosts_rock_ground_and_steel_moves_in_sandstorm(PokemonType type, int expectedPower)
    {
        var move = new MoveDefinition("strike", "Strike", type, MoveCategory.Physical, 50);
        var user = With(Mon("User", 100, moves: [Weather(Battles.Weather.Sandstorm), move]), "sandforce");
        var target = Mon("Target", 50, moves: [Splash]) with { Types = [PokemonType.Steel] };
        var battle = Singles([user], [target]);
        battle.Start();
        Turn(battle);

        Assert.Equal(ExpectedDamage(move, expectedPower, user, target), DamageTo(Turn(battle, p1Move: 1), SideId.P2));
    }

    [Fact]
    public void Aerilate_turns_normal_moves_into_boosted_flying_moves()
    {
        var user = With(Mon("Pinsir", 100, moves: [Tackle]), "aerilate");
        var ghost = Mon("Gengar", 50, moves: [Splash]) with { Types = [PokemonType.Ghost] };
        var battle = Singles([user], [ghost]);
        battle.Start();

        var events = Turn(battle);

        Assert.DoesNotContain(events, e => e is Immune);
        // Gen 6 Aerilate: x1.3 (5325/4096), so Tackle becomes a 65 power Flying move.
        Assert.Equal(ExpectedDamage(Tackle, 65, user, ghost, PokemonType.Flying), DamageTo(events, SideId.P2));
    }

    [Theory]
    [InlineData(10, 65)]
    [InlineData(100, 50)]
    public void Analytic_boosts_the_last_pokemon_to_move(int userSpeed, int expectedPower)
    {
        var move = Normal(50);
        var user = With(Mon("Magnezone", userSpeed, moves: [move]), "analytic");
        var target = Mon("Target", 50, moves: [Splash]);
        var battle = Singles([user], [target]);
        battle.Start();

        Assert.Equal(ExpectedDamage(move, expectedPower, user, target), DamageTo(Turn(battle), SideId.P2));
    }

    [Fact]
    public void Scrappy_lets_normal_moves_hit_ghosts_in_battle()
    {
        var battle = Singles(
            [With(Mon("Kangaskhan", 100), "scrappy")],
            [Mon("Gengar", 50, moves: [Splash]) with { Types = [PokemonType.Ghost] }]);
        battle.Start();

        Assert.Contains(Turn(battle), e => e is DamageDealt { Side: SideId.P2 });
    }

    [Theory]
    [InlineData("compoundeyes", 70, MoveCategory.Special, 90, true)] // 70 x1.3 = 91
    [InlineData("compoundeyes", 70, MoveCategory.Special, 91, false)]
    [InlineData("hustle", 100, MoveCategory.Physical, 79, true)] // 100 x0.8 = 80
    [InlineData("hustle", 100, MoveCategory.Physical, 80, false)]
    public void Accuracy_abilities_of_the_attacker(string ability, int accuracy, MoveCategory category, int roll, bool hits)
    {
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, category, 50, Accuracy: accuracy);
        var battle = Singles(
            [With(Mon("User", 100, moves: [move]), ability)], [Mon("Target", 50, moves: [Splash])], new PercentRolls(roll));
        battle.Start();

        Assert.Equal(hits, Turn(battle).Any(e => e is DamageDealt { Side: SideId.P2 }));
    }

    [Fact]
    public void Hustle_does_not_lower_the_accuracy_of_special_moves()
    {
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Special, 50, Accuracy: 100);
        var battle = Singles(
            [With(Mon("User", 100, moves: [move]), "hustle")], [Mon("Target", 50, moves: [Splash])], new PercentRolls());
        battle.Start();

        Assert.Contains(Turn(battle), e => e is DamageDealt { Side: SideId.P2 });
    }

    [Theory]
    [InlineData("sandveil", Battles.Weather.Sandstorm, 79, true)] // 100 x0.8 = 80
    [InlineData("sandveil", Battles.Weather.Sandstorm, 80, false)]
    [InlineData("snowcloak", Battles.Weather.Hail, 80, false)]
    public void Sand_veil_and_snow_cloak_raise_evasion_in_their_weather(string ability, Weather weather, int roll, bool hits)
    {
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50, Accuracy: 100);
        var battle = Singles(
            [Mon("User", 100, moves: [Weather(weather), move]) with { Types = [PokemonType.Steel, PokemonType.Ice] }],
            [With(Mon("Target", 50, moves: [Splash]), ability)],
            new PercentRolls(roll));
        battle.Start();
        Turn(battle);

        Assert.Equal(hits, Turn(battle, p1Move: 1).Any(e => e is DamageDealt { Side: SideId.P2 }));
    }

    [Theory]
    [InlineData(MoveCategory.Status, 49, true)] // status moves drop to 50% accuracy
    [InlineData(MoveCategory.Status, 50, false)]
    public void Wonder_skin_halves_the_accuracy_of_status_moves(MoveCategory category, int roll, bool lands)
    {
        var growl = new MoveDefinition("growl", "Growl", PokemonType.Normal, category, 0, Target: MoveTarget.Normal,
            Accuracy: 100, Boosts: [Stage(BoostStat.Attack, -1)]);
        var battle = Singles(
            [Mon("User", 100, moves: [growl])], [With(Mon("Target", 50, moves: [Splash]), "wonderskin")], new PercentRolls(roll));
        battle.Start();

        Assert.Equal(lands, Turn(battle).Any(e => e is StatStageChanged { Side: SideId.P2 }));
    }

    [Fact]
    public void Wonder_skin_ignores_damaging_moves()
    {
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50, Accuracy: 100);
        var battle = Singles(
            [Mon("User", 100, moves: [move])], [With(Mon("Target", 50, moves: [Splash]), "wonderskin")], new PercentRolls());
        battle.Start();

        Assert.Contains(Turn(battle), e => e is DamageDealt { Side: SideId.P2 });
    }

    [Theory]
    [InlineData("keeneye")]
    [InlineData("unaware")]
    public void Keen_eye_and_unaware_ignore_the_targets_evasion(string ability)
    {
        var doubleTeam = new MoveDefinition("doubleteam", "Double Team", PokemonType.Normal, MoveCategory.Status, 0,
            Target: MoveTarget.Self, Boosts: [Stage(BoostStat.Evasion, 1)]);
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50, Accuracy: 100);
        var battle = Singles(
            [With(Mon("User", 10, moves: [move]), ability)], [Mon("Target", 50, moves: [doubleTeam])], new PercentRolls());
        battle.Start();

        Assert.Contains(Turn(battle), e => e is DamageDealt { Side: SideId.P2 });
    }

    [Fact]
    public void Unaware_targets_ignore_the_attackers_accuracy_boosts()
    {
        var hone = new MoveDefinition("honeclaws", "Hone Claws", PokemonType.Dark, MoveCategory.Status, 0,
            Target: MoveTarget.Self, Boosts: [Stage(BoostStat.Accuracy, 1)]);
        var move = new MoveDefinition("strike", "Strike", PokemonType.Normal, MoveCategory.Physical, 50, Accuracy: 70);
        // With +1 accuracy the move would be at 93%; against Unaware it stays at 70%, so 70 misses.
        var battle = Singles(
            [Mon("User", 100, moves: [hone, move])], [With(Mon("Target", 50, moves: [Splash]), "unaware")], new PercentRolls(70));
        battle.Start();
        Turn(battle);

        Assert.DoesNotContain(Turn(battle, p1Move: 1), e => e is DamageDealt { Side: SideId.P2 });
    }

    [Fact]
    public void No_guard_moves_and_moves_against_no_guard_never_miss()
    {
        var guillotine = new MoveDefinition("guillotine", "Guillotine", PokemonType.Normal, MoveCategory.Physical, 0,
            Accuracy: 30, Ohko: true);
        var thunder = new MoveDefinition("thunder", "Thunder", PokemonType.Electric, MoveCategory.Special, 110, Accuracy: 70);
        var battle = Singles(
            [With(Mon("Machamp", 10, hp: 1000, moves: [guillotine]), "noguard")],
            [Mon("Target", 50, moves: [thunder]), Mon("Spare", 40)],
            new PercentRolls());
        battle.Start();

        var events = Describe(Turn(battle));

        Assert.Contains("P1.0 Machamp damaged", events);
        Assert.Contains("P2.0 one-hit KO", events);
    }

    [Fact]
    public void No_guard_reaches_a_semi_invulnerable_target()
    {
        var fly = new MoveDefinition("fly", "Fly", PokemonType.Flying, MoveCategory.Physical, 90, Accuracy: 95, Charge: true);
        var battle = Singles(
            [Mon("Pidgeot", 100, moves: [fly])], [With(Mon("Machamp", 50), "noguard")], new PercentRolls());
        battle.Start();

        Assert.Contains("P1.0 Pidgeot damaged", Describe(Turn(battle)));
    }

    [Theory]
    [InlineData("chlorophyll", Battles.Weather.Sun)]
    [InlineData("swiftswim", Battles.Weather.Rain)]
    [InlineData("sandrush", Battles.Weather.Sandstorm)]
    public void Weather_speed_abilities_double_speed(string ability, Weather weather)
    {
        var user = With(Mon("User", 60, moves: [Weather(weather), Tackle]) with { Types = [PokemonType.Steel] }, ability);
        var battle = Singles([user], [Mon("Foe", 100) with { Types = [PokemonType.Steel] }]);
        battle.Start();
        Turn(battle);

        var events = Describe(Turn(battle, p1Move: 1));

        Assert.Equal(120, battle.GetSide(SideId.P1).Active[0]!.Speed);
        Assert.Equal("P1.0 User uses tackle", events[0]);
    }

    [Fact]
    public void Quick_feet_boosts_speed_when_statused_and_ignores_paralysis()
    {
        var paralyze = new MoveDefinition("inflict", "Inflict", PokemonType.Normal, MoveCategory.Status, 0,
            Target: MoveTarget.Normal, Status: MajorStatus.Paralysis);
        var battle = Singles(
            [With(Mon("Jolteon", 100, moves: [Splash]), "quickfeet")], [Mon("Foe", 50, moves: [paralyze])]);
        battle.Start();

        Turn(battle);

        Assert.Equal(150, battle.GetSide(SideId.P1).Active[0]!.Speed);
    }

    [Fact]
    public void Friend_guard_reduces_damage_to_its_allies()
    {
        var battle = Doubles(
            [Mon("User", 100), Mon("Ally", 90, moves: [Splash])],
            [Mon("Target", 50, moves: [Splash]), With(Mon("Clefable", 40, moves: [Splash]), "friendguard")]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0)));

        var events = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0))).Events;

        Assert.Equal(27, DamageTo(events, SideId.P2)); // 36 x0.75
    }

    [Fact]
    public void Solar_power_costs_an_eighth_of_max_hp_each_turn_in_sun()
    {
        var battle = Singles(
            [With(Mon("Charizard", 100, moves: [Weather(Battles.Weather.Sun)]), "solarpower")],
            [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Assert.Contains("P1.0 Charizard hurt by Sun 50", Describe(Turn(battle)));
    }

    [Theory]
    [InlineData("sandveil", Battles.Weather.Sandstorm)]
    [InlineData("sandrush", Battles.Weather.Sandstorm)]
    [InlineData("sandforce", Battles.Weather.Sandstorm)]
    [InlineData("snowcloak", Battles.Weather.Hail)]
    public void Some_abilities_shelter_from_weather_damage(string ability, Weather weather)
    {
        var battle = Singles(
            [With(Mon("User", 100, moves: [Weather(weather)]), ability)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var events = Describe(Turn(battle));

        Assert.Contains($"P2.0 Foe hurt by {weather} 25", events);
        Assert.DoesNotContain(events, e => e.StartsWith("P1.0 User hurt"));
    }
}
