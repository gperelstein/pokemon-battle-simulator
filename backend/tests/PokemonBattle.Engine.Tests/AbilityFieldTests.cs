using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Switch-in, switch-out and end-of-turn abilities and Cloud Nine, checked against Showdown's
/// data/abilities.ts (Gen 6 mods), sim/battle-actions.ts runSwitch/switchIn and sim/field.ts.
/// </summary>
public class AbilityFieldTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition Ember = new("ember", "Ember", PokemonType.Fire, MoveCategory.Special, 40);

    private static readonly MoveDefinition WaterGun = new("watergun", "Water Gun", PokemonType.Water, MoveCategory.Special, 40);

    private static readonly MoveDefinition Earthquake = new(
        "earthquake", "Earthquake", PokemonType.Ground, MoveCategory.Physical, 100, Target: MoveTarget.AllAdjacent);

    private static readonly MoveDefinition HyperBeam = new(
        "hyperbeam", "Hyper Beam", PokemonType.Normal, MoveCategory.Special, 150, Recharge: true);

    private static readonly MoveDefinition Spore = new(
        "spore", "Spore", PokemonType.Grass, MoveCategory.Status, 0, Target: MoveTarget.Normal, Status: MajorStatus.Sleep);

    private static MoveDefinition Weather(Weather weather) => new(
        "weather", "Weather", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.All, SetsWeather: weather);

    private static MoveDefinition Inflict(MajorStatus status) => new(
        "inflict", "Inflict", PokemonType.Normal, MoveCategory.Status, 0, Priority: 1, Target: MoveTarget.Normal,
        Status: status);

    private static MoveDefinition Damager(int amount) =>
        new("dragonrage", "Dragon Rage", PokemonType.Dragon, MoveCategory.Special, 0, Priority: 1, FixedDamage: amount);

    private static PokemonSet With(string species, int speed, string ability, params MoveDefinition[] moves) =>
        WithAbility(Mon(species, speed, moves: moves.Length == 0 ? [Splash] : moves), ability);

    private static IReadOnlyList<string> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return Describe(battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events);
    }

    private static BattlePokemon Active(Battle battle, SideId side, int slot = 0) => battle.GetSide(side).Active[slot]!;

    [Fact]
    public void Intimidate_lowers_the_foes_attack_when_the_battle_starts()
    {
        var battle = Singles([With("Gyarados", 81, "intimidate")], [Mon("Foe", 50, moves: [Splash])]);

        var events = Describe(battle.Start());

        Assert.Equal(
            ["P1.0 in Gyarados", "P2.0 in Foe", "P1.0 Gyarados intimidate activates", "P2.0 Foe Attack -1 = -1", "turn 1"],
            events.Take(5));
    }

    [Fact]
    public void Intimidate_hits_every_adjacent_foe_and_respects_their_abilities()
    {
        var battle = Doubles(
            [With("Gyarados", 81, "intimidate"), Mon("Ally", 50, moves: [Splash])],
            [Mon("Left", 50, moves: [Splash]), With("Right", 40, "hypercutter")]);

        var events = Describe(battle.Start());

        Assert.Contains("P2.0 Left Attack -1 = -1", events);
        Assert.Contains("P2.1 Right hypercutter prevents stat drop", events);
        Assert.DoesNotContain(events, e => e.StartsWith("P1.1 Ally Attack"));
    }

    [Fact]
    public void Switch_in_abilities_also_trigger_after_a_switch_during_the_turn()
    {
        var battle = Singles([Mon("Lead", 100, moves: [Splash]), With("Gyarados", 81, "intimidate")], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle);
        battle.Submit(SideId.P1, Switch(0, 1));
        var switching = Describe(battle.Submit(SideId.P2, Move()).Events);

        Assert.Equal(
            ["P1.0 out Lead", "P1.0 in Gyarados", "P1.0 Gyarados intimidate activates", "P2.0 Foe Attack -1 = -1"],
            switching.Take(4));
        Assert.Empty(events.Where(e => e.Contains("intimidate")));
    }

    [Fact]
    public void Switch_in_abilities_of_all_leads_resolve_by_speed()
    {
        var battle = Singles([With("Slow", 10, "intimidate")], [With("Fast", 90, "intimidate")]);

        var events = Describe(battle.Start());

        Assert.True(events.ToList().IndexOf("P2.0 Fast intimidate activates") < events.ToList().IndexOf("P1.0 Slow intimidate activates"));
    }

    [Fact]
    public void Drought_starts_sun_on_switch_in()
    {
        var battle = Singles([With("Ninetales", 100, "drought")], [Mon("Foe", 50, moves: [Splash])]);

        var events = Describe(battle.Start());

        Assert.Equal(["P1.0 Ninetales drought activates", "weather Sun starts"], events.Skip(2).Take(2));
        Assert.Equal(Battles.Weather.Sun, battle.Weather);
    }

    [Theory]
    [InlineData(100, 120, "Attack")] // weaker Defense: Attack +1
    [InlineData(120, 100, "SpecialAttack")]
    [InlineData(100, 100, "SpecialAttack")] // a tie favours Special Attack
    public void Download_raises_the_stat_that_beats_the_foes_weaker_defense(int defense, int specialDefense, string raised)
    {
        var foe = Mon("Foe", 50, moves: [Splash]) with { Stats = new Stats(400, 100, defense, 100, specialDefense, 50) };
        var battle = Singles([With("Porygon", 40, "download")], [foe]);

        var events = Describe(battle.Start());

        Assert.Contains($"P1.0 Porygon {raised} +1 = 1", events);
    }

    [Fact]
    public void Cloud_nine_cancels_the_effects_of_weather_but_not_its_countdown()
    {
        var foe = With("Ninetales", 50, "drought", Ember, Weather(Battles.Weather.Sandstorm));
        var user = With("Golduck", 100, "cloudnine");
        var battle = Singles([user], [foe]);

        var start = Describe(battle.Start());
        battle.Submit(SideId.P1, Move());
        var ember = battle.Submit(SideId.P2, Move()).Events;
        var sand = Turn(battle, p2Move: 1);

        Assert.Contains("P1.0 Golduck cloudnine activates", start);
        // No sun boost: Ember deals its clear-weather damage.
        var clear = new DamageCalculator(TestTypeChart.Gen6Sample).Calculate(foe, user, Ember, false, new MaxRandom()).Damage;
        Assert.Equal(clear, ember.OfType<DamageDealt>().Single(d => d.Side == SideId.P1).Amount);
        Assert.Contains("weather Sandstorm continues", sand);
        Assert.DoesNotContain(sand, e => e.Contains("hurt by Sandstorm"));
    }

    [Fact]
    public void Cloud_nine_also_stops_weather_speed_abilities()
    {
        var battle = Singles(
            [With("Golduck", 100, "cloudnine")], [With("Venusaur", 80, "chlorophyll", Weather(Battles.Weather.Sun))]);
        battle.Start();
        Turn(battle);

        Assert.Equal(80, Active(battle, SideId.P2).Speed);
    }

    [Fact]
    public void Trace_copies_the_foes_ability_and_runs_it()
    {
        var battle = Singles([With("Porygon", 40, "trace", Earthquake)], [With("Gyarados", 81, "intimidate", Earthquake)]);

        var events = Describe(battle.Start());

        Assert.Contains("P1.0 Porygon traced intimidate from P2.0", events);
        Assert.Contains("P2.0 Gyarados Attack -1 = -1", events);
        Assert.Equal("intimidate", Active(battle, SideId.P1).Ability);
    }

    [Fact]
    public void Trace_cannot_copy_untraceable_abilities()
    {
        // Trace is untraceable in the catalog (Showdown's "notrace" flag) on both sides.
        var battle = Singles(
            [With("Porygon", 40, "trace") with { AbilityTraceable = false }],
            [WithAbility(Mon("Porygon2", 50, moves: [Splash]), "trace") with { AbilityTraceable = false }]);

        var events = Describe(battle.Start());

        Assert.DoesNotContain(events, e => e.Contains("traced"));
        Assert.Equal("trace", Active(battle, SideId.P1).Ability);
    }

    [Fact]
    public void A_traced_ability_is_lost_when_switching_out()
    {
        var battle = Singles(
            [With("Porygon", 40, "trace") with { AbilityTraceable = false }, Mon("Spare", 10)], [With("Gengar", 81, "levitate")]);
        battle.Start();

        battle.Submit(SideId.P1, Switch(0, 1));
        battle.Submit(SideId.P2, Move());

        Assert.Equal("trace", battle.GetSide(SideId.P1).Team[0].Ability);
    }

    [Theory]
    [InlineData(PokemonType.Water, true)]
    [InlineData(PokemonType.Grass, false)]
    public void Anticipation_shudders_at_super_effective_foe_moves(PokemonType foeMoveType, bool shudders)
    {
        var foeMove = new MoveDefinition("hit", "Hit", foeMoveType, MoveCategory.Special, 40);
        var holder = With("Charmander", 40, "anticipation") with { Types = [PokemonType.Fire] };
        var battle = Singles([holder], [Mon("Foe", 50, moves: [foeMove])]);

        Assert.Equal(shudders, Describe(battle.Start()).Contains("P1.0 Charmander anticipation activates"));
    }

    [Fact]
    public void Forewarn_reveals_the_foes_most_powerful_move()
    {
        var battle = Singles([With("Drowzee", 40, "forewarn")], [Mon("Snorlax", 50, moves: [Tackle, HyperBeam, Splash])]);

        Assert.Contains("P1.0 Drowzee forewarns hyperbeam of P2.0", Describe(battle.Start()));
    }

    [Fact]
    public void Mold_breaker_announces_itself_on_switch_in()
    {
        var battle = Singles([WithAbility(Mon("Pinsir", 85, moves: [Splash]), "moldbreaker", breakable: false)], [Mon("Foe", 50)]);

        Assert.Contains("P1.0 Pinsir moldbreaker activates", Describe(battle.Start()));
    }

    [Fact]
    public void Natural_cure_heals_status_when_switching_out()
    {
        var battle = Singles(
            [With("Starmie", 100, "naturalcure"), Mon("Spare", 10)], [Mon("Foe", 50, moves: [Inflict(MajorStatus.Burn), Splash])]);
        battle.Start();
        Turn(battle);

        battle.Submit(SideId.P1, Switch(0, 1));
        battle.Submit(SideId.P2, Move(moveIndex: 1));

        Assert.Null(battle.GetSide(SideId.P1).Team[0].Status);
    }

    [Fact]
    public void Regenerator_restores_a_third_of_max_hp_when_switching_out()
    {
        var battle = Singles(
            [With("Slowbro", 100, "regenerator"), Mon("Spare", 10)], [Mon("Foe", 50, moves: [Damager(300), Splash])]);
        battle.Start();
        Turn(battle);

        battle.Submit(SideId.P1, Switch(0, 1));
        var events = Describe(battle.Submit(SideId.P2, Move(moveIndex: 1)).Events);

        Assert.Contains("P1.0 Slowbro healed 133", events); // floor(400 / 3)
        Assert.Equal(233, battle.GetSide(SideId.P1).Team[0].CurrentHp);
    }

    [Theory]
    [InlineData("raindish", Battles.Weather.Rain)]
    [InlineData("icebody", Battles.Weather.Hail)]
    public void Rain_dish_and_ice_body_heal_a_sixteenth_in_their_weather(string ability, Weather weather)
    {
        var battle = Singles(
            [With("Holder", 100, ability, Weather(weather))], [Mon("Foe", 50, moves: [Damager(100)])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal([$"P1.0 Holder {ability} activates", "P1.0 Holder healed 25"],
            events.SkipWhile(e => e != $"weather {weather} continues").Skip(1).Take(2));
        Assert.DoesNotContain(events, e => e.StartsWith("P1.0 Holder hurt"));
    }

    [Fact]
    public void Hydration_cures_status_in_rain_before_status_damage()
    {
        var battle = Singles(
            [With("Lapras", 100, "hydration", Weather(Battles.Weather.Rain))], [Mon("Foe", 50, moves: [Inflict(MajorStatus.Burn)])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P1.0 Lapras cured of Burn", events);
        Assert.DoesNotContain(events, e => e.Contains("hurt by Burn"));
    }

    [Theory]
    [InlineData(32, true)] // 33% chance
    [InlineData(33, false)]
    public void Shed_skin_may_cure_status_at_the_end_of_the_turn(int roll, bool cured)
    {
        var battle = Singles(
            [With("Dragonair", 100, "shedskin")], [Mon("Foe", 50, moves: [Inflict(MajorStatus.Burn)])], new PercentRolls(roll));
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(cured, events.Contains("P1.0 Dragonair cured of Burn"));
    }

    [Theory]
    [InlineData(2, true)] // 30% chance per ally
    [InlineData(3, false)]
    public void Healer_may_cure_an_allys_status_in_doubles(int roll, bool cured)
    {
        var battle = Doubles(
            [Mon("Ally", 100, moves: [Splash]), With("Chansey", 90, "healer")],
            [Mon("Foe", 50, moves: [Inflict(MajorStatus.Burn)]), Mon("Other", 40, moves: [Splash])],
            new PercentRolls().Script(0, 10, roll));
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0)));

        var events = Describe(battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0))).Events);

        Assert.Equal(cured, events.Contains("P1.0 Ally cured of Burn"));
    }

    [Theory]
    [InlineData(4, 1)] // the counter drops by 2: 4 -> 2 -> 0, so one turn asleep instead of three
    [InlineData(2, 0)] // wakes on its first attempt
    public void Early_bird_wakes_up_twice_as_fast(int durationRoll, int turnsAsleep)
    {
        var battle = Singles(
            [Mon("Foe", 100, moves: [Spore, Splash])], [With("Doduo", 50, "earlybird", Tackle)],
            new PercentRolls().Script(2, 5, durationRoll));
        battle.Start();

        var events = new List<string>(Turn(battle));
        for (var i = 0; i < 2; i++)
        {
            events.AddRange(Turn(battle, p1Move: 1));
        }

        Assert.Equal(turnsAsleep, events.Count(e => e == "P2.0 Doduo can't move (Sleep)"));
    }

    [Fact]
    public void An_ability_that_blocks_a_status_cures_it_as_soon_as_it_can()
    {
        // Mold Breaker puts the Insomnia holder to sleep; Insomnia cures it right after the move.
        var battle = Singles(
            [WithAbility(Mon("Pinsir", 100, moves: [Spore]), "moldbreaker", breakable: false)],
            [With("Hypno", 50, "insomnia", Tackle)]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(
            ["P2.0 Hypno gets Sleep", "P2.0 Hypno insomnia activates", "P2.0 Hypno cured of Sleep", "P2.0 Hypno uses tackle"],
            events.Skip(1).Take(4));
    }
}
