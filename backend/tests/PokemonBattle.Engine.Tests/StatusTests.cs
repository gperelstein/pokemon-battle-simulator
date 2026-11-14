using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>Gen 6 major status conditions, checked against Showdown's data/conditions.ts and data/mods/gen6.</summary>
public class StatusTests
{
    private static readonly MoveDefinition ThunderWave = new(
        "thunderwave", "Thunder Wave", PokemonType.Electric, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Accuracy: 100, Status: MajorStatus.Paralysis, IgnoreImmunity: false);

    private static readonly MoveDefinition StunSpore = new(
        "stunspore", "Stun Spore", PokemonType.Grass, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Accuracy: 75, Status: MajorStatus.Paralysis, Powder: true);

    private static readonly MoveDefinition WillOWisp = new(
        "willowisp", "Will-O-Wisp", PokemonType.Fire, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Accuracy: 85, Status: MajorStatus.Burn);

    private static readonly MoveDefinition Toxic = new(
        "toxic", "Toxic", PokemonType.Poison, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Accuracy: 90, Status: MajorStatus.Toxic);

    private static readonly MoveDefinition PoisonGas = new(
        "poisongas", "Poison Gas", PokemonType.Poison, MoveCategory.Status, 0, Target: MoveTarget.AllAdjacentFoes,
        Status: MajorStatus.Poison);

    private static readonly MoveDefinition Spore = new(
        "spore", "Spore", PokemonType.Grass, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Accuracy: 100, Status: MajorStatus.Sleep, Powder: true);

    private static readonly MoveDefinition Snore = new(
        "snore", "Snore", PokemonType.Normal, MoveCategory.Special, 50, Accuracy: 100, SleepUsable: true);

    private static readonly MoveDefinition IceBeam = new(
        "icebeam", "Ice Beam", PokemonType.Ice, MoveCategory.Special, 90, Accuracy: 100,
        Secondaries: [new SecondaryEffect(10, Status: MajorStatus.Freeze)]);

    private static readonly MoveDefinition FlameWheel = new(
        "flamewheel", "Flame Wheel", PokemonType.Fire, MoveCategory.Physical, 60, Accuracy: 100, Defrost: true);

    private static readonly MoveDefinition Ember = new(
        "ember", "Ember", PokemonType.Fire, MoveCategory.Special, 40, Accuracy: 100);

    private static readonly MoveDefinition Nuzzle = new(
        "nuzzle", "Nuzzle", PokemonType.Electric, MoveCategory.Physical, 20, Accuracy: 100,
        Secondaries: [new SecondaryEffect(100, Status: MajorStatus.Paralysis)]);

    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static PokemonSet Typed(PokemonSet set, params PokemonType[] types) => set with { Types = types };

    private static IReadOnlyList<string> PlayTurn(Battle battle, Decision? p1 = null, Decision? p2 = null)
    {
        battle.Submit(SideId.P1, p1 ?? Move());
        return Describe(battle.Submit(SideId.P2, p2 ?? Move()).Events);
    }

    private static BattlePokemon Active(Battle battle, SideId side) => battle.GetSide(side).Active[0]!;

    [Fact]
    public void Thunder_wave_paralyzes_and_paralysis_quarters_speed()
    {
        var battle = Singles([Mon("Pikachu", 90, moves: [ThunderWave])], [Mon("Rattata", 72)]);
        battle.Start();

        var events = PlayTurn(battle);

        Assert.Equal(
            ["P1.0 Pikachu uses thunderwave", "P2.0 Rattata gets Paralysis", "P2.0 Rattata uses tackle"],
            events.Take(3));
        Assert.Equal(MajorStatus.Paralysis, Active(battle, SideId.P2).Status);
        // Gen 6: floor(72 * 25 / 100) = 18 (Gen 7 onwards halves instead).
        Assert.Equal(18, Active(battle, SideId.P2).Speed);
    }

    [Fact]
    public void Paralysis_fully_stops_the_move_one_time_in_four()
    {
        var rolls = new PercentRolls().Script(0, 4, 3, 0);
        var battle = Singles([Mon("Pikachu", 90, moves: [ThunderWave, Tackle])], [Mon("Rattata", 72)], rolls);
        battle.Start();
        PlayTurn(battle);

        var events = PlayTurn(battle, Move(moveIndex: 1));

        Assert.Equal(
            ["P1.0 Pikachu uses tackle", "P2.0 Rattata damaged", "P2.0 Rattata can't move (Paralysis)"],
            events.Take(3));
        Assert.DoesNotContain("P2.0 Rattata uses tackle", events);
        Assert.Equal(0, rolls.Remaining);
    }

    [Fact]
    public void Status_moves_fail_against_a_pokemon_that_already_has_a_status()
    {
        var battle = Singles([Mon("Pikachu", 90, moves: [ThunderWave])], [Mon("Rattata", 72)]);
        battle.Start();
        PlayTurn(battle);

        var events = PlayTurn(battle);

        Assert.Equal(["P1.0 Pikachu uses thunderwave", "P1.0 thunderwave failed"], events.Take(2));
    }

    [Theory]
    [InlineData("thunderwave", PokemonType.Electric, false)] // status immunity (Gen 6+)
    [InlineData("thunderwave", PokemonType.Ground, false)] // Thunder Wave keeps its type immunity
    [InlineData("stunspore", PokemonType.Grass, false)] // powder immunity is checked before accuracy
    [InlineData("willowisp", PokemonType.Fire, true)] // status immunity is checked after accuracy
    [InlineData("toxic", PokemonType.Steel, true)]
    [InlineData("toxic", PokemonType.Poison, true)]
    public void Some_types_are_immune_to_status_moves(string moveId, PokemonType targetType, bool rollsAccuracy)
    {
        var move = new[] { ThunderWave, StunSpore, WillOWisp, Toxic }.Single(m => m.Id == moveId);
        var rolls = rollsAccuracy ? new PercentRolls(0) : new PercentRolls();
        var battle = Singles([Mon("Mew", 100, moves: [move])], [Typed(Mon("Target", 50), targetType)], rolls);
        battle.Start();

        var events = PlayTurn(battle);

        Assert.Equal([$"P1.0 Mew uses {moveId}", "P2.0 Target immune"], events.Take(2));
        Assert.Null(Active(battle, SideId.P2).Status);
        Assert.Equal(0, rolls.Remaining);
    }

    [Fact]
    public void Burn_deals_an_eighth_of_max_hp_at_the_end_of_the_turn()
    {
        var battle = Singles([Mon("Charmander", 65, moves: [WillOWisp])], [Mon("Rattata", 50)], new PercentRolls(0));
        battle.Start();

        var events = PlayTurn(battle);

        Assert.Equal(
            [
                "P1.0 Charmander uses willowisp", "P2.0 Rattata gets Burn", "P2.0 Rattata uses tackle",
                "P1.0 Charmander damaged", "P2.0 Rattata hurt by Burn 50", "turn 2",
            ],
            events.Take(6));
    }

    [Fact]
    public void Burn_halves_the_damage_of_physical_moves()
    {
        var battle = Singles([Mon("Charmander", 65, moves: [WillOWisp])], [Mon("Rattata", 50)], new PercentRolls(0));
        battle.Start();

        PlayTurn(battle);

        // Unburned Tackle deals 36 (see DamageCalculatorTests); burned: pokeRound(36 * 0.5) = 18.
        Assert.Equal(400 - 18, Active(battle, SideId.P1).CurrentHp);
    }

    [Fact]
    public void Poison_deals_an_eighth_and_toxic_grows_by_a_sixteenth_each_turn()
    {
        var battle = Singles(
            [Typed(Mon("Arbok", 80, moves: [Toxic, Splash]), PokemonType.Poison)],
            [Mon("Rattata", 50, hp: 300)]);
        battle.Start();

        var first = PlayTurn(battle);
        var second = PlayTurn(battle, Move(moveIndex: 1));
        var third = PlayTurn(battle, Move(moveIndex: 1));

        // trunc(max(1, 300 / 16) * n): 18, 37 (not 2 * 18), 56.
        Assert.Contains("P2.0 Rattata hurt by Toxic 18", first);
        Assert.Contains("P2.0 Rattata hurt by Toxic 37", second);
        Assert.Contains("P2.0 Rattata hurt by Toxic 56", third);

        var poisoned = Singles([Mon("Arbok", 80, moves: [PoisonGas])], [Mon("Rattata", 50)]);
        poisoned.Start();
        Assert.Contains("P2.0 Rattata hurt by Poison 50", PlayTurn(poisoned));
    }

    [Fact]
    public void Toxic_counter_restarts_after_switching_but_the_status_stays()
    {
        var battle = Singles(
            [Typed(Mon("Arbok", 80, moves: [Toxic, Splash]), PokemonType.Poison)],
            [Mon("Rattata", 50, hp: 300), Mon("Pidgey", 56)]);
        battle.Start();
        PlayTurn(battle);
        PlayTurn(battle, Move(moveIndex: 1));

        PlayTurn(battle, Move(moveIndex: 1), Switch(0, 1));
        var back = PlayTurn(battle, Move(moveIndex: 1), Switch(0, 0));

        Assert.Contains("P2.0 Rattata hurt by Toxic 18", back);
        Assert.Equal(MajorStatus.Toxic, Active(battle, SideId.P2).Status);
    }

    [Fact]
    public void Poison_damage_comes_before_burn_damage_whatever_the_speed()
    {
        var battle = Singles(
            [Mon("Charmander", 65, moves: [WillOWisp])],
            [Typed(Mon("Weezing", 60, moves: [PoisonGas]), PokemonType.Poison)],
            new PercentRolls(0));
        battle.Start();

        var events = PlayTurn(battle);

        Assert.True(
            events.ToList().IndexOf("P1.0 Charmander hurt by Poison 50") <
            events.ToList().IndexOf("P2.0 Weezing hurt by Burn 50"));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    public void Sleep_lasts_one_to_three_turns(int durationRoll, int turnsAsleep)
    {
        var rolls = new PercentRolls().Script(2, 5, durationRoll);
        var battle = Singles([Mon("Parasect", 30, moves: [Spore, Splash])], [Mon("Snorlax", 20)], rolls);
        battle.Start();

        var turns = new List<IReadOnlyList<string>> { PlayTurn(battle) };
        for (var i = 0; i < turnsAsleep; i++)
        {
            turns.Add(PlayTurn(battle, Move(moveIndex: 1)));
        }

        Assert.Contains("P2.0 Snorlax gets Sleep", turns[0]);
        Assert.All(turns.Take(turnsAsleep), t => Assert.Contains("P2.0 Snorlax can't move (Sleep)", t));
        Assert.Equal(
            ["P2.0 Snorlax cured of Sleep", "P2.0 Snorlax uses tackle"],
            turns[^1].SkipWhile(e => !e.Contains("Snorlax cured")).Take(2));
    }

    [Fact]
    public void Sleep_usable_moves_work_while_asleep()
    {
        var battle = Singles([Mon("Parasect", 30, moves: [Spore])], [Mon("Snorlax", 20, moves: [Snore])]);
        battle.Start();

        var events = PlayTurn(battle);

        Assert.Equal(
            ["P2.0 Snorlax gets Sleep", "P2.0 Snorlax can't move (Sleep)", "P2.0 Snorlax uses snore"],
            events.Skip(1).Take(3));
    }

    [Fact]
    public void Frozen_pokemon_cannot_move_until_they_thaw_one_time_in_five()
    {
        var rolls = new PercentRolls(0).Script(0, 5, 4, 0);
        var battle = Singles([Mon("Lapras", 60, moves: [IceBeam, Splash])], [Mon("Snorlax", 30)], rolls);
        battle.Start();

        var frozen = PlayTurn(battle);
        var thawed = PlayTurn(battle, Move(moveIndex: 1));

        Assert.Equal(["P2.0 Snorlax gets Freeze", "P2.0 Snorlax can't move (Freeze)"], frozen.Skip(2).Take(2));
        Assert.Equal(["P2.0 Snorlax cured of Freeze", "P2.0 Snorlax uses tackle"], thawed.Skip(1).Take(2));
        Assert.Equal(0, rolls.Remaining);
    }

    [Fact]
    public void Ice_types_cannot_be_frozen()
    {
        var battle = Singles(
            [Mon("Lapras", 60, moves: [IceBeam])], [Typed(Mon("Jynx", 30), PokemonType.Ice)], new PercentRolls(0));
        battle.Start();

        var events = PlayTurn(battle);

        Assert.DoesNotContain(events, e => e.Contains("gets"));
        Assert.Null(Active(battle, SideId.P2).Status);
    }

    [Fact]
    public void Defrosting_moves_thaw_their_user_before_it_moves()
    {
        // Frozen by Ice Beam, then Flame Wheel thaws Ponyta without a thaw roll.
        var rolls = new PercentRolls(0).Script(0, 5);
        var battle = Singles([Mon("Lapras", 90, moves: [IceBeam])], [Mon("Ponyta", 30, moves: [FlameWheel])], rolls);
        battle.Start();

        var events = PlayTurn(battle);

        Assert.Equal(
            ["P2.0 Ponyta gets Freeze", "P2.0 Ponyta cured of Freeze", "P2.0 Ponyta uses flamewheel"],
            events.Skip(2).Take(3));
    }

    [Fact]
    public void Fire_moves_thaw_the_target_they_hit()
    {
        var battle = Singles(
            [Mon("Lapras", 90, moves: [IceBeam, Ember])], [Mon("Snorlax", 30)], new PercentRolls(0));
        battle.Start();
        PlayTurn(battle);

        var events = PlayTurn(battle, Move(moveIndex: 1));

        Assert.Equal(["P2.0 Snorlax damaged", "P2.0 Snorlax cured of Freeze"], events.Skip(1).Take(2));
    }

    [Fact]
    public void Secondary_statuses_do_nothing_to_a_pokemon_that_already_has_one()
    {
        var battle = Singles(
            [Mon("Charmander", 65, moves: [WillOWisp, Nuzzle])], [Mon("Rattata", 50)], new PercentRolls(0));
        battle.Start();
        PlayTurn(battle);

        var events = PlayTurn(battle, Move(moveIndex: 1));

        Assert.DoesNotContain(events, e => e.Contains("gets") || e.Contains("failed"));
        Assert.Equal(MajorStatus.Burn, Active(battle, SideId.P2).Status);
    }

    [Fact]
    public void Poison_type_users_never_miss_with_toxic()
    {
        var poisonUser = Singles(
            [Typed(Mon("Arbok", 80, moves: [Toxic]), PokemonType.Poison)], [Mon("Rattata", 50)], new PercentRolls());
        poisonUser.Start();

        Assert.Contains("P2.0 Rattata gets Toxic", PlayTurn(poisonUser));
    }
}
