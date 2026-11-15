using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Berries and the abilities that interact with them (Unnerve, Gluttony, Harvest), checked against
/// Showdown's data/items.ts with the gen6 item mod (Figy, Wiki, Mago, Aguav and Iapapa heal 1/8 at
/// half HP; Jaboca and Rowap without the "attacker still active" check), data/abilities.ts and
/// sim/pokemon.ts eatItem. Every Pokémon has 400 HP unless stated; Tackle at level 50 with 100
/// Attack against 100 Defense deals 36 at the max roll (STAB included), Water Gun 19 (no STAB).
/// </summary>
public class BerryTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition WaterGun = new(
        "watergun", "Water Gun", PokemonType.Water, MoveCategory.Special, 40);

    private static readonly MoveDefinition HydroPump = new(
        "hydropump", "Hydro Pump", PokemonType.Water, MoveCategory.Special, 120);

    private static readonly MoveDefinition Sunny = new(
        "sunnyday", "Sunny Day", PokemonType.Fire, MoveCategory.Status, 0, Target: MoveTarget.All, SetsWeather: Weather.Sun);

    private static MoveDefinition Damager(int amount) =>
        new("dragonrage", "Dragon Rage", PokemonType.Dragon, MoveCategory.Special, 0, Priority: 1, FixedDamage: amount);

    private static MoveDefinition PhysicalDamager(int amount) =>
        new("seismictoss", "Seismic Toss", PokemonType.Fighting, MoveCategory.Physical, 0, Priority: 1, FixedDamage: amount);

    private static MoveDefinition Inflict(MajorStatus status) => new(
        "inflict", "Inflict", PokemonType.Normal, MoveCategory.Status, 0, Priority: 1, Target: MoveTarget.Normal,
        Status: status);

    private static PokemonSet Holding(PokemonSet set, string item) => set with { Item = item };

    private static PokemonSet Typed(PokemonSet set, params PokemonType[] types) => set with { Types = types };

    private static IReadOnlyList<string> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return Describe(battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events);
    }

    private static BattlePokemon Active(Battle battle, SideId side, int slot = 0) => battle.GetSide(side).Active[slot]!;

    private static IEnumerable<string> Of(IEnumerable<string> events, string prefix) =>
        events.Where(e => e.StartsWith(prefix) && !e.Contains(" uses "));

    // ---- Status cures (onUpdate; Lum also onAfterSetStatus) ----

    [Theory]
    [InlineData("cheriberry", MajorStatus.Paralysis)]
    [InlineData("chestoberry", MajorStatus.Sleep)]
    [InlineData("pechaberry", MajorStatus.Poison)]
    [InlineData("pechaberry", MajorStatus.Toxic)]
    [InlineData("rawstberry", MajorStatus.Burn)]
    [InlineData("aspearberry", MajorStatus.Freeze)]
    [InlineData("lumberry", MajorStatus.Paralysis)]
    [InlineData("lumberry", MajorStatus.Sleep)]
    [InlineData("lumberry", MajorStatus.Toxic)]
    [InlineData("lumberry", MajorStatus.Burn)]
    [InlineData("lumberry", MajorStatus.Freeze)]
    public void Status_berries_cure_their_status_right_after_it_is_inflicted(string berry, MajorStatus status)
    {
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), berry)], [Mon("Foe", 100, moves: [Inflict(status)])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal([$"P1.0 User gets {status}", $"P1.0 User {berry} consumed", $"P1.0 User cured of {status}"],
            Of(events, "P1.0 User").Take(3));
        Assert.Null(Active(battle, SideId.P1).Status);
        Assert.Null(Active(battle, SideId.P1).Item);
    }

    [Theory]
    [InlineData("cheriberry", MajorStatus.Burn)]
    [InlineData("chestoberry", MajorStatus.Paralysis)]
    [InlineData("pechaberry", MajorStatus.Sleep)]
    [InlineData("rawstberry", MajorStatus.Poison)]
    [InlineData("aspearberry", MajorStatus.Burn)]
    public void Status_berries_ignore_the_other_statuses(string berry, MajorStatus status)
    {
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), berry)], [Mon("Foe", 100, moves: [Inflict(status)])]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("consumed"));
        Assert.Equal(status, Active(battle, SideId.P1).Status);
        Assert.Equal(berry, Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void Lum_berry_cures_as_soon_as_the_status_is_set_while_other_berries_wait_for_the_update()
    {
        // A Dark move with a sure burn against a Justified holder: Lum (onAfterSetStatus) cures before
        // the DamagingHit reaction, Rawst (onUpdate) only after it.
        var bite = new MoveDefinition("bite", "Bite", PokemonType.Dark, MoveCategory.Physical, 40, Priority: 1,
            Secondaries: [new SecondaryEffect(100, Status: MajorStatus.Burn)]);
        var events = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var berry in new[] { "lumberry", "rawstberry" })
        {
            var holder = WithAbility(Holding(Mon("User", 50, moves: [Splash]), berry), "justified");
            var battle = Singles([holder], [Mon("Foe", 100, moves: [bite])]);
            battle.Start();
            events[berry] = Of(Turn(battle), "P1.0 User").Where(e => !e.Contains("damaged")).ToArray();
        }

        Assert.Equal(
            ["P1.0 User gets Burn", "P1.0 User lumberry consumed", "P1.0 User cured of Burn", "P1.0 User justified activates",
                "P1.0 User Attack +1 = 1"],
            events["lumberry"].Take(5));
        Assert.Equal(
            ["P1.0 User gets Burn", "P1.0 User justified activates", "P1.0 User Attack +1 = 1", "P1.0 User rawstberry consumed",
                "P1.0 User cured of Burn"],
            events["rawstberry"].Take(5));
    }

    [Fact]
    public void Lum_berry_is_eaten_after_synchronize_passes_the_status_back()
    {
        var holder = WithAbility(Holding(Mon("User", 50, moves: [Splash]), "lumberry"), "synchronize");
        var battle = Singles([holder], [Mon("Foe", 100, moves: [Inflict(MajorStatus.Paralysis)])]);
        battle.Start();

        var events = Turn(battle).Where(e => !e.Contains(" uses ")).ToArray();

        Assert.Equal(
            ["P1.0 User gets Paralysis", "P1.0 User synchronize activates", "P2.0 Foe gets Paralysis", "P1.0 User lumberry consumed",
                "P1.0 User cured of Paralysis"],
            events.Take(5));
    }

    // ---- HP berries (onUpdate) ----

    [Theory]
    [InlineData("oranberry", 10)]
    [InlineData("sitrusberry", 100)] // floor(400 / 4)
    [InlineData("figyberry", 50)] // Gen 6: floor(400 / 8) at half HP
    [InlineData("wikiberry", 50)]
    [InlineData("magoberry", 50)]
    [InlineData("aguavberry", 50)]
    [InlineData("iapapaberry", 50)]
    public void Hp_berries_heal_once_their_holder_is_at_half_hp_or_less(string berry, int heal)
    {
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), berry)], [Mon("Foe", 100, moves: [Damager(200)])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P1.0 User damaged", $"P1.0 User {berry} consumed", $"P1.0 User healed {heal}"], Of(events, "P1.0 User"));
        Assert.Equal(200 + heal, Active(battle, SideId.P1).CurrentHp);
    }

    [Theory]
    [InlineData("oranberry")]
    [InlineData("sitrusberry")]
    [InlineData("figyberry")]
    public void Hp_berries_wait_while_their_holder_is_above_half_hp(string berry)
    {
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), berry)], [Mon("Foe", 100, moves: [Damager(199)])]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("consumed"));
        Assert.Equal(201, Active(battle, SideId.P1).CurrentHp);
        Assert.Equal(berry, Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void Hp_berries_also_act_after_end_of_turn_damage()
    {
        var battle = Singles(
            [Holding(Mon("User", 50, moves: [Splash]), "sitrusberry")], [Mon("Foe", 100, moves: [Damager(150), Inflict(MajorStatus.Burn)])]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p2Move: 1);

        // 250 - 50 burn = 200, then +100.
        Assert.Equal(["P1.0 User hurt by Burn 50", "P1.0 User sitrusberry consumed", "P1.0 User healed 100"],
            Of(events, "P1.0 User").Where(e => !e.Contains("gets")));
        Assert.Equal(300, Active(battle, SideId.P1).CurrentHp);
    }

    // ---- Pinch berries (onUpdate, a quarter of max HP; half with Gluttony) ----

    [Theory]
    [InlineData("liechiberry", BoostStat.Attack)]
    [InlineData("ganlonberry", BoostStat.Defense)]
    [InlineData("salacberry", BoostStat.Speed)]
    [InlineData("petayaberry", BoostStat.SpecialAttack)]
    [InlineData("apicotberry", BoostStat.SpecialDefense)]
    public void Pinch_berries_raise_a_stat_once_their_holder_is_at_a_quarter_of_its_hp(string berry, BoostStat stat)
    {
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), berry)], [Mon("Foe", 100, moves: [Damager(300)])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P1.0 User damaged", $"P1.0 User {berry} consumed", $"P1.0 User {stat} +1 = 1"], Of(events, "P1.0 User"));
        Assert.Equal(1, Active(battle, SideId.P1).GetStage(stat));
    }

    [Theory]
    [InlineData(299, null, false)] // 101 > 400 / 4
    [InlineData(200, null, false)]
    [InlineData(200, "gluttony", true)] // 200 <= 400 / 2 with Gluttony
    [InlineData(199, "gluttony", false)]
    public void Gluttony_makes_pinch_berries_act_at_half_hp(int damage, string? ability, bool eaten)
    {
        var holder = Holding(Mon("User", 50, moves: [Splash]), "liechiberry") with { Ability = ability };
        var battle = Singles([holder], [Mon("Foe", 100, moves: [Damager(damage)])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(eaten, events.Contains("P1.0 User liechiberry consumed"));
        Assert.Equal(eaten ? 1 : 0, Active(battle, SideId.P1).GetStage(BoostStat.Attack));
    }

    [Fact]
    public void Gluttony_does_not_change_the_gen_6_hp_berry_thresholds()
    {
        var holder = WithAbility(Holding(Mon("User", 50, moves: [Splash]), "figyberry"), "gluttony", breakable: false);
        var battle = Singles([holder], [Mon("Foe", 100, moves: [Damager(199)])]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("consumed"));
    }

    [Fact]
    public void A_pinch_berry_at_plus_six_is_still_eaten_and_says_the_stat_will_not_go_higher()
    {
        var bellyDrum = new MoveDefinition("bellydrum", "Belly Drum", PokemonType.Normal, MoveCategory.Status, 0, Priority: 2,
            Target: MoveTarget.Self, Boosts: [Stage(BoostStat.Attack, 12)]);
        var battle = Singles([Holding(Mon("User", 50, moves: [bellyDrum]), "liechiberry")], [Mon("Foe", 100, moves: [Damager(300)])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P1.0 User liechiberry consumed", "P1.0 User Attack won't go higher"], Of(events, "P1.0 User").TakeLast(2));
    }

    [Fact]
    public void Starf_berry_raises_a_random_stat_by_two_among_those_below_plus_six()
    {
        // Candidates in Showdown's boost order: Attack, Defense, Sp. Atk, Sp. Def, Speed. Defense is
        // already at +6, so index 1 is Sp. Atk.
        var ironDefense = new MoveDefinition("irondefense", "Iron Defense", PokemonType.Steel, MoveCategory.Status, 0,
            Priority: 2, Target: MoveTarget.Self, Boosts: [Stage(BoostStat.Defense, 12)]);
        var random = new ScriptedRandom(1);
        var battle = Battle.Create(BattleFormat.Singles, [Holding(Mon("User", 50, moves: [ironDefense]), "starfberry")],
            [Mon("Foe", 100, moves: [Damager(300)])], TestTypeChart.Gen6Sample, new FallbackRandom(random, (0, 4)));
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P1.0 User starfberry consumed", "P1.0 User SpecialAttack +2 = 2"], Of(events, "P1.0 User").TakeLast(2));
        Assert.Equal([(0, 4)], random.Calls);
    }

    [Fact]
    public void Lansat_berry_gives_the_focus_energy_effect()
    {
        var battle = Singles(
            [Holding(Mon("User", 50, moves: [Splash, Tackle]), "lansatberry")], [Mon("Foe", 100, moves: [Damager(300), Splash])],
            new PercentRolls().Script(0, 2, 0));
        battle.Start();

        var first = Turn(battle);
        var second = Turn(battle, p1Move: 1, p2Move: 1);

        Assert.Equal(["P1.0 User lansatberry consumed", "P1.0 User focusenergy starts"],
            Of(first, "P1.0 User").Where(e => !e.Contains("damaged")).Take(2));
        // Crit stage 1 + 2 = 3: one in two (the [0, 2) roll), instead of one in sixteen.
        Assert.Contains("P2.0 critical hit", second);
    }

    // ---- Micle (onResidual) ----

    [Fact]
    public void Micle_berry_is_eaten_at_the_end_of_the_turn_and_raises_the_next_move_accuracy()
    {
        var shaky = new MoveDefinition("shaky", "Shaky", PokemonType.Normal, MoveCategory.Physical, 40, Accuracy: 50);
        // 50 x 4915/4096 = 60: the roll of 59 hits (it would miss at 50).
        var battle = Singles(
            [Holding(Mon("User", 50, moves: [Splash, shaky]), "micleberry")], [Mon("Foe", 100, moves: [Damager(300), Splash])],
            new PercentRolls(59));
        battle.Start();

        var first = Turn(battle);
        var second = Turn(battle, p1Move: 1, p2Move: 1);

        Assert.Equal("P1.0 User micleberry consumed", Of(first, "P1.0 User").Last());
        Assert.Equal(["P1.0 User micleberry activates", "P2.0 Foe damaged"],
            second.SkipWhile(e => !e.Contains("micleberry")).Take(2));
    }

    // ---- Type-resist berries and Chilan (onSourceModifyDamage) ----

    [Fact]
    public void Resist_berries_halve_a_super_effective_hit_of_their_type()
    {
        var holder = Typed(Holding(Mon("User", 50, moves: [Splash]), "passhoberry"), PokemonType.Fire);
        var battle = Singles([holder], [Mon("Foe", 100, moves: [WaterGun])]);
        battle.Start();

        var events = Turn(battle);

        // 19 x 2 = 38, halved to 19.
        Assert.Equal(
            ["P1.0 effectiveness x2", "P1.0 User passhoberry consumed", "P1.0 User damaged"],
            events.SkipWhile(e => !e.Contains("effectiveness")).Take(3));
        Assert.Equal(381, Active(battle, SideId.P1).CurrentHp);
    }

    [Theory]
    [InlineData("passhoberry", PokemonType.Normal)] // neutral Water hit
    [InlineData("occaberry", PokemonType.Fire)] // super effective, wrong type
    public void Resist_berries_ignore_other_hits(string berry, PokemonType holderType)
    {
        var holder = Typed(Holding(Mon("User", 50, moves: [Splash]), berry), holderType);
        var battle = Singles([holder], [Mon("Foe", 100, moves: [WaterGun])]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("consumed"));
        Assert.Equal(berry, Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void Chilan_berry_halves_any_normal_hit()
    {
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), "chilanberry")], [Mon("Foe", 100, moves: [Tackle])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P1.0 User chilanberry consumed", "P1.0 User damaged"], Of(events, "P1.0 User"));
        Assert.Equal(400 - 18, Active(battle, SideId.P1).CurrentHp);
    }

    // ---- Enigma (onHit), Jaboca / Rowap (DamagingHit), Kee / Maranga (AfterMoveSecondary) ----

    [Fact]
    public void Enigma_berry_heals_a_quarter_after_a_super_effective_hit()
    {
        var holder = Typed(Holding(Mon("User", 50, moves: [Splash]), "enigmaberry"), PokemonType.Fire);
        var battle = Singles([holder], [Mon("Foe", 100, moves: [HydroPump, WaterGun])]);
        battle.Start();

        var events = Turn(battle);

        // Hydro Pump: (22 x 120 / 50 + 2) x 2 = 108, then +100.
        Assert.Equal(["P1.0 User damaged", "P1.0 User enigmaberry consumed", "P1.0 User healed 100"], Of(events, "P1.0 User"));
        Assert.Equal(392, Active(battle, SideId.P1).CurrentHp);
    }

    [Fact]
    public void Enigma_berry_ignores_neutral_hits()
    {
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), "enigmaberry")], [Mon("Foe", 100, moves: [HydroPump])]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("consumed"));
    }

    [Theory]
    [InlineData("jabocaberry", true)]
    [InlineData("rowapberry", false)]
    public void Jaboca_and_rowap_hurt_an_attacker_of_their_category_by_an_eighth(string berry, bool physical)
    {
        var move = physical ? PhysicalDamager(50) : Damager(50);
        var other = physical ? Damager(50) : PhysicalDamager(50);
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), berry)], [Mon("Foe", 100, moves: [other, move])]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("consumed"));
        var events = Turn(battle, p2Move: 1);

        Assert.Equal([$"P1.0 User {berry} consumed", $"P2.0 Foe hurt by {berry} 50"],
            events.Where(e => e.Contains(berry)));
        Assert.Equal(350, Active(battle, SideId.P2).CurrentHp);
    }

    [Fact]
    public void Jaboca_berry_works_even_when_the_hit_knocks_its_holder_out()
    {
        var battle = Singles(
            [Holding(Mon("User", 50, moves: [Splash]), "jabocaberry"), Mon("Bench", 50)], [Mon("Foe", 100, moves: [PhysicalDamager(400)])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P1.0 User damaged", "P1.0 User fainted", "P1.0 User jabocaberry consumed"], Of(events, "P1.0 User"));
        Assert.Contains("P2.0 Foe hurt by jabocaberry 50", events);
    }

    [Fact]
    public void Jaboca_berry_stays_against_a_magic_guard_attacker()
    {
        var foe = WithAbility(Mon("Foe", 100, moves: [PhysicalDamager(50)]), "magicguard", breakable: false);
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), "jabocaberry")], [foe]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("jabocaberry"));
        Assert.Equal("jabocaberry", Active(battle, SideId.P1).Item);
    }

    [Theory]
    [InlineData("keeberry", true, BoostStat.Defense)]
    [InlineData("marangaberry", false, BoostStat.SpecialDefense)]
    public void Kee_and_maranga_raise_a_defense_after_a_hit_of_their_category(string berry, bool physical, BoostStat stat)
    {
        var move = physical ? PhysicalDamager(50) : Damager(50);
        var other = physical ? Damager(50) : PhysicalDamager(50);
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), berry)], [Mon("Foe", 100, moves: [other, move])]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("consumed"));
        var events = Turn(battle, p2Move: 1);

        Assert.Equal(["P1.0 User damaged", $"P1.0 User {berry} consumed", $"P1.0 User {stat} +1 = 1"], Of(events, "P1.0 User"));
    }

    // ---- Custap (onFractionalPriority) ----

    [Fact]
    public void Custap_berry_lets_its_holder_move_first_in_its_bracket_once_at_a_quarter_hp()
    {
        var battle = Singles(
            [Holding(Mon("User", 50, moves: [Tackle]), "custapberry")], [Mon("Foe", 100, moves: [Damager(300), Tackle])]);
        battle.Start();
        Turn(battle);
        Assert.Equal("custapberry", Active(battle, SideId.P1).Item);

        var events = Turn(battle, p2Move: 1).Where(e => !e.StartsWith("turn")).ToArray();

        Assert.Equal(["P1.0 User custapberry consumed", "P1.0 User uses tackle"], events.Take(2));
        Assert.Null(Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void Custap_berry_waits_above_a_quarter_hp_without_gluttony()
    {
        var battle = Singles(
            [Holding(Mon("User", 50, moves: [Tackle]), "custapberry")], [Mon("Foe", 100, moves: [Damager(200), Tackle])]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p2Move: 1).Where(e => e.Contains(" uses ")).ToArray();

        Assert.Equal(["P2.0 Foe uses tackle", "P1.0 User uses tackle"], events);
    }

    // ---- Unnerve ----

    [Fact]
    public void Unnerve_is_announced_on_switch_in_and_stops_foes_from_eating_berries()
    {
        var unnerve = WithAbility(Mon("Foe", 100, moves: [Damager(200)]), "unnerve", breakable: false);
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), "sitrusberry")], [unnerve, Mon("Bench", 10, moves: [Splash])]);

        Assert.Contains("P2.0 Foe unnerve activates", Describe(battle.Start()));
        Assert.DoesNotContain(Turn(battle), e => e.Contains("consumed"));

        battle.Submit(SideId.P1, Move());
        var events = Describe(battle.Submit(SideId.P2, Switch(0, 1)).Events);

        // Once the Unnerve holder is gone, the next Update eats the berry.
        Assert.Equal(["P2.0 out Foe", "P2.0 in Bench", "P1.0 User sitrusberry consumed", "P1.0 User healed 100"],
            events.Where(e => !e.StartsWith("turn")).Take(4));
    }

    [Fact]
    public void Unnerve_stops_resist_berries_too()
    {
        var holder = Typed(Holding(Mon("User", 50, moves: [Splash]), "passhoberry"), PokemonType.Fire);
        var foe = WithAbility(Mon("Foe", 100, moves: [WaterGun]), "unnerve", breakable: false);
        var battle = Singles([holder], [foe]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("consumed"));
        Assert.Equal(400 - 38, Active(battle, SideId.P1).CurrentHp);
    }

    [Fact]
    public void Unnerve_does_not_stop_berry_juice_which_is_not_a_berry()
    {
        var foe = WithAbility(Mon("Foe", 100, moves: [Damager(200)]), "unnerve", breakable: false);
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), "berryjuice")], [foe]);
        battle.Start();

        Assert.Contains("P1.0 User berryjuice consumed", Turn(battle));
        Assert.Equal(220, Active(battle, SideId.P1).CurrentHp);
    }

    // ---- Harvest (residual order 28, sub-order 2) ----

    [Fact]
    public void Harvest_restores_the_eaten_berry_half_of_the_time()
    {
        var holder = WithAbility(Holding(Mon("User", 50, moves: [Splash]), "sitrusberry"), "harvest", breakable: false);
        var battle = Singles([holder], [Mon("Foe", 100, moves: [Damager(250)])], new PercentRolls().Script(0, 2, 0));
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P1.0 User harvest activates", "P1.0 User receives sitrusberry"], Of(events, "P1.0 User").TakeLast(2));
        Assert.Equal("sitrusberry", Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void Harvest_always_works_in_sun_and_the_restored_berry_can_be_eaten_at_once()
    {
        // MaxRandom fails the 1-in-2 roll; the sun skips it. 400 - 300 = 100, Sitrus to 200, which is
        // still half HP once Harvest gives the berry back, so the Update after the residuals eats it.
        var holder = WithAbility(Holding(Mon("User", 50, moves: [Splash]), "sitrusberry"), "harvest", breakable: false);
        var battle = Singles([holder], [Mon("Foe", 100, moves: [Sunny, Damager(300)])]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p2Move: 1);

        Assert.Equal(
            ["P1.0 User damaged", "P1.0 User sitrusberry consumed", "P1.0 User healed 100", "P1.0 User harvest activates",
                "P1.0 User receives sitrusberry", "P1.0 User sitrusberry consumed", "P1.0 User healed 100"],
            Of(events, "P1.0 User"));
        Assert.Equal(300, Active(battle, SideId.P1).CurrentHp);
    }

    [Fact]
    public void Harvest_fails_its_roll_out_of_the_sun()
    {
        var holder = WithAbility(Holding(Mon("User", 50, moves: [Splash]), "sitrusberry"), "harvest", breakable: false);
        var battle = Singles([holder], [Mon("Foe", 100, moves: [Damager(250)])]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("harvest"));
        Assert.Null(Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void Harvest_does_not_restore_items_that_are_not_berries_nor_roll_without_a_berry()
    {
        var holder = WithAbility(Holding(Mon("User", 50, moves: [Splash]), "berryjuice"), "harvest", breakable: false);
        var random = new RecordingRandom();
        var battle = Singles([holder], [Mon("Foe", 100, moves: [Damager(250)])], random);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("harvest"));
        Assert.DoesNotContain((0, 2), random.Calls);
    }

    /// <summary>Returns <see cref="MaxRandom"/>'s values and records every requested range.</summary>
    private sealed class RecordingRandom : IBattleRandom
    {
        public List<(int Min, int Max)> Calls { get; } = [];

        public int Next(int minInclusive, int maxExclusive)
        {
            Calls.Add((minInclusive, maxExclusive));
            return maxExclusive - 1;
        }
    }

    /// <summary>Sends the scripted range to <paramref name="scripted"/> and answers every other one with the max.</summary>
    private sealed class FallbackRandom(ScriptedRandom scripted, (int Min, int Max) range) : IBattleRandom
    {
        public int Next(int minInclusive, int maxExclusive) =>
            (minInclusive, maxExclusive) == range ? scripted.Next(minInclusive, maxExclusive) : maxExclusive - 1;
    }
}
