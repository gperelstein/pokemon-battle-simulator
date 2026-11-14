using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>Gen 6 weather, checked against Showdown's data/conditions.ts and the weather moves' code.</summary>
public class WeatherTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static MoveDefinition WeatherMove(Weather weather) => weather switch
    {
        Weather.Sun => new("sunnyday", "Sunny Day", PokemonType.Fire, MoveCategory.Status, 0, Target: MoveTarget.All,
            SetsWeather: Weather.Sun),
        Weather.Rain => new("raindance", "Rain Dance", PokemonType.Water, MoveCategory.Status, 0, Target: MoveTarget.All,
            SetsWeather: Weather.Rain),
        Weather.Sandstorm => new("sandstorm", "Sandstorm", PokemonType.Rock, MoveCategory.Status, 0, Target: MoveTarget.All,
            SetsWeather: Weather.Sandstorm),
        _ => new("hail", "Hail", PokemonType.Ice, MoveCategory.Status, 0, Target: MoveTarget.All, SetsWeather: Weather.Hail),
    };

    private static MoveDefinition Setup(Weather? weather) => weather is { } w ? WeatherMove(w) : Splash;

    private static readonly MoveDefinition Thunder = new(
        "thunder", "Thunder", PokemonType.Electric, MoveCategory.Special, 110, Accuracy: 70);

    private static readonly MoveDefinition Hurricane = new(
        "hurricane", "Hurricane", PokemonType.Flying, MoveCategory.Special, 110, Accuracy: 70);

    private static readonly MoveDefinition Blizzard = new(
        "blizzard", "Blizzard", PokemonType.Ice, MoveCategory.Special, 110, Target: MoveTarget.AllAdjacentFoes, Accuracy: 70);

    private static readonly MoveDefinition WeatherBall = new(
        "weatherball", "Weather Ball", PokemonType.Normal, MoveCategory.Special, 50, Accuracy: 100);

    private static readonly MoveDefinition SolarBeam = new(
        "solarbeam", "Solar Beam", PokemonType.Grass, MoveCategory.Special, 120, Accuracy: 100);

    private static readonly MoveDefinition Growth = new(
        "growth", "Growth", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self,
        Boosts: [Stage(BoostStat.Attack, 1), Stage(BoostStat.SpecialAttack, 1)]);

    private static readonly MoveDefinition Moonlight = new(
        "moonlight", "Moonlight", PokemonType.Fairy, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition Recover = new(
        "recover", "Recover", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self,
        Heal: new HealFraction(1, 2));

    private static readonly MoveDefinition IceBeam = new(
        "icebeam", "Ice Beam", PokemonType.Ice, MoveCategory.Special, 90, Accuracy: 100,
        Secondaries: [new SecondaryEffect(100, Status: MajorStatus.Freeze)]);

    private static readonly MoveDefinition Burn = new(
        "inflict", "Inflict", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Normal, Status: MajorStatus.Burn);

    private static MoveDefinition Damager(int amount) =>
        new("dragonrage", "Dragon Rage", PokemonType.Dragon, MoveCategory.Special, 0, Priority: 1, FixedDamage: amount);

    private static IReadOnlyList<string> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return Describe(battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events);
    }

    private static int ExpectedDamage(MoveDefinition move, PokemonType type, int basePower, Weather? weather, PokemonSet user, PokemonSet target) =>
        new DamageCalculator(TestTypeChart.Gen6Sample)
            .Calculate(user, target, move with { Id = "fixed", Type = type, BasePower = basePower }, false, new MaxRandom(),
                weather: weather)
            .Damage;

    [Fact]
    public void Weather_lasts_five_turns_counting_the_turn_it_starts()
    {
        var battle = Singles([Mon("Ninetales", 100, moves: [WeatherMove(Weather.Sun), Splash])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var turns = new List<IReadOnlyList<string>> { Turn(battle) };
        for (var i = 0; i < 4; i++)
        {
            turns.Add(Turn(battle, p1Move: 1));
        }

        Assert.Equal(["P1.0 Ninetales uses sunnyday", "weather Sun starts"], turns[0].Take(2));
        Assert.All(turns.Take(4), t => Assert.Contains("weather Sun continues", t));
        Assert.Contains("weather Sun ends", turns[4]);
        Assert.DoesNotContain("weather Sun continues", turns[4]);
        Assert.Null(battle.Weather);
    }

    [Fact]
    public void Weather_moves_fail_when_that_weather_is_already_active_and_replace_any_other()
    {
        var battle = Singles(
            [Mon("Politoed", 100, moves: [WeatherMove(Weather.Sun), WeatherMove(Weather.Rain)])],
            [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        Turn(battle);

        var again = Turn(battle);
        var rain = Turn(battle, p1Move: 1);

        Assert.Equal(["P1.0 Politoed uses sunnyday", "P1.0 sunnyday failed"], again.Take(2));
        Assert.Equal(["P1.0 Politoed uses raindance", "weather Rain starts"], rain.Take(2));
        Assert.Equal(Weather.Rain, battle.Weather);
    }

    [Fact]
    public void Sandstorm_hurts_non_immune_pokemon_by_speed_before_status_damage()
    {
        var mixed = Singles(
            [Mon("Fast", 100, moves: [WeatherMove(Weather.Sandstorm)])],
            [Mon("Slow", 50, moves: [Burn])]);
        mixed.Start();

        var rockSetter = Singles(
            [Mon("Golem", 100, moves: [WeatherMove(Weather.Sandstorm)]) with { Types = [PokemonType.Rock] }],
            [Mon("Foe", 50, moves: [Splash])]);
        rockSetter.Start();

        var bothHurt = Turn(mixed);
        var onlyFoe = Turn(rockSetter);

        Assert.Equal(
            ["weather Sandstorm continues", "P1.0 Fast hurt by Sandstorm 25", "P2.0 Slow hurt by Sandstorm 25",
             "P1.0 Fast hurt by Burn 50"],
            bothHurt.SkipWhile(e => e != "weather Sandstorm continues").Take(4));
        Assert.Contains("P2.0 Foe hurt by Sandstorm 25", onlyFoe);
        Assert.DoesNotContain(onlyFoe, e => e.StartsWith("P1.0 Golem hurt"));
    }

    [Fact]
    public void Hail_spares_ice_types_and_weather_damage_stops_when_it_ends()
    {
        var battle = Singles(
            [Mon("Lapras", 100, moves: [WeatherMove(Weather.Hail), Splash]) with { Types = [PokemonType.Ice] }],
            [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var events = new List<string>(Turn(battle));
        for (var i = 0; i < 5; i++)
        {
            events.AddRange(Turn(battle, p1Move: 1));
        }

        Assert.Equal(4, events.Count(e => e == "P2.0 Foe hurt by Hail 25"));
        Assert.DoesNotContain(events, e => e.StartsWith("P1.0 Lapras hurt"));
    }

    [Fact]
    public void Nothing_can_be_frozen_under_the_sun()
    {
        var battle = Singles(
            [Mon("Ninetales", 100, moves: [WeatherMove(Weather.Sun), IceBeam])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p1Move: 1);

        Assert.Contains("P2.0 Foe damaged", events);
        Assert.DoesNotContain(events, e => e.Contains("gets Freeze"));
    }

    [Theory]
    [InlineData("thunder", Weather.Rain, new int[0], true)] // never misses in rain
    [InlineData("thunder", Weather.Sun, new[] { 49 }, true)] // 50% in sun
    [InlineData("thunder", Weather.Sun, new[] { 50 }, false)]
    [InlineData("hurricane", Weather.Rain, new int[0], true)]
    [InlineData("hurricane", Weather.Sun, new[] { 50 }, false)]
    [InlineData("blizzard", Weather.Hail, new int[0], true)] // never misses in hail
    [InlineData("thunder", null, new[] { 69 }, true)]
    [InlineData("thunder", null, new[] { 70 }, false)]
    public void Weather_changes_the_accuracy_of_some_moves(string moveId, Weather? weather, int[] rolls, bool hits)
    {
        var move = new[] { Thunder, Hurricane, Blizzard }.Single(m => m.Id == moveId);
        var random = new PercentRolls(rolls);
        var battle = Singles(
            [Mon("User", 100, moves: [Setup(weather), move]) with { Types = [PokemonType.Ice] }],
            [Mon("Foe", 50, moves: [Splash]) with { Types = [PokemonType.Ice] }],
            random);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p1Move: 1);

        Assert.Equal(hits ? "P2.0 Foe damaged" : "P2.0 Foe avoided", events[1]);
        Assert.Equal(0, random.Remaining);
    }

    [Theory]
    [InlineData(Weather.Sun, PokemonType.Fire)]
    [InlineData(Weather.Rain, PokemonType.Water)]
    [InlineData(Weather.Sandstorm, PokemonType.Rock)]
    [InlineData(Weather.Hail, PokemonType.Ice)]
    public void Weather_ball_doubles_and_takes_the_weather_type(Weather weather, PokemonType type)
    {
        // Steel user and target: no STAB and immune to every weather's damage.
        var user = Mon("Castform", 100, moves: [WeatherMove(weather), WeatherBall]) with { Types = [PokemonType.Steel] };
        var target = Mon("Foe", 50, moves: [Splash]) with { Types = [PokemonType.Steel] };
        var battle = Singles([user], [target]);
        battle.Start();
        Turn(battle);
        battle.Submit(SideId.P1, Move(moveIndex: 1));

        var events = battle.Submit(SideId.P2, Move()).Events;

        var dealt = events.OfType<DamageDealt>().Single(d => d.Side == SideId.P2).Amount;
        Assert.Equal(ExpectedDamage(WeatherBall, type, 100, weather, user, target), dealt);
    }

    [Fact]
    public void Weather_ball_in_rain_is_a_water_move_for_effectiveness()
    {
        var battle = Singles(
            [Mon("Castform", 100, moves: [WeatherMove(Weather.Rain), WeatherBall])],
            [Mon("Arcanine", 50, moves: [Splash]) with { Types = [PokemonType.Fire] }]);
        battle.Start();
        Turn(battle);

        Assert.Contains("P2.0 effectiveness x2", Turn(battle, p1Move: 1));
    }

    [Theory]
    [InlineData(Weather.Rain, 60)]
    [InlineData(Weather.Sandstorm, 60)]
    [InlineData(Weather.Hail, 60)]
    [InlineData(Weather.Sun, 120)]
    public void Solar_beam_is_weakened_by_rain_sand_and_hail(Weather weather, int expectedPower)
    {
        var user = Mon("Venusaur", 100, moves: [WeatherMove(weather), SolarBeam]) with { Types = [PokemonType.Steel] };
        var target = Mon("Foe", 50, moves: [Splash]) with { Types = [PokemonType.Steel] };
        var battle = Singles([user], [target]);
        battle.Start();
        Turn(battle);
        battle.Submit(SideId.P1, Move(moveIndex: 1));

        var events = battle.Submit(SideId.P2, Move()).Events;

        var dealt = events.OfType<DamageDealt>().Single(d => d.Side == SideId.P2).Amount;
        Assert.Equal(ExpectedDamage(SolarBeam, PokemonType.Grass, expectedPower, weather, user, target), dealt);
    }

    [Theory]
    [InlineData(Weather.Sun, 2)]
    [InlineData(null, 1)]
    public void Growth_raises_attack_and_special_attack_two_stages_in_sun(Weather? weather, int stages)
    {
        var battle = Singles([Mon("Vileplume", 100, moves: [Setup(weather), Growth])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        Turn(battle);

        Turn(battle, p1Move: 1);

        var user = battle.GetSide(SideId.P1).Active[0]!;
        Assert.Equal(stages, user.GetStage(BoostStat.Attack));
        Assert.Equal(stages, user.GetStage(BoostStat.SpecialAttack));
    }

    [Theory]
    [InlineData(null, 200)] // pokeRound(400 * 2048 / 4096)
    [InlineData(Weather.Sun, 267)] // pokeRound(400 * 2732 / 4096): Showdown's 0.667 factor
    [InlineData(Weather.Rain, 100)]
    [InlineData(Weather.Sandstorm, 100)]
    [InlineData(Weather.Hail, 100)]
    public void Moonlight_heals_more_in_sun_and_less_in_other_weather(Weather? weather, int healed)
    {
        var user = Mon("Clefable", 100, moves: [Setup(weather), Moonlight]) with { Types = [PokemonType.Steel, PokemonType.Ice] };
        var battle = Singles([user], [Mon("Foe", 50, moves: [Damager(300), Splash])]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p1Move: 1, p2Move: 1);

        Assert.Contains($"P1.0 Clefable healed {healed}", events);
    }

    [Fact]
    public void Recover_heals_half_of_max_hp_rounded_and_fails_at_full_hp()
    {
        var battle = Singles(
            [Mon("Starmie", 100, hp: 401, moves: [Recover])],
            [Mon("Foe", 50, moves: [Splash, Damager(300)])]);
        battle.Start();

        var full = Turn(battle);
        var hurt = Turn(battle, p2Move: 1);

        Assert.Equal(["P1.0 Starmie uses recover", "P1.0 recover failed"], full.Take(2));
        // Math.round(401 / 2) = 201.
        Assert.Contains("P1.0 Starmie healed 201", hurt);
    }
}
