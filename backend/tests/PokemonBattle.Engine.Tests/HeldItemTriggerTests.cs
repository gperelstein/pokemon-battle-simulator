using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Self-triggered held items and non-berry consumables, checked against Showdown's data/items.ts
/// with the Gen 6 mods (Rocky Helmet from gen6, Berry Juice made standard by gen8), the weather
/// durationCallbacks of data/conditions.ts and sim/battle.ts (fieldEvent residual order, damage and
/// heal rounding), sim/battle-actions.ts (hit steps, DamagingHit) and sim/pokemon.ts (isGrounded,
/// runImmunity, useItem). Every Pokémon has 400 HP unless stated; Tackle at level 50 with 100
/// Attack against 100 Defense deals 36 at the max roll.
/// </summary>
public class HeldItemTriggerTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition ContactTackle = Tackle with { Flags = MoveFlags.Contact };

    private static readonly MoveDefinition GroundStrike = new(
        "groundstrike", "Ground Strike", PokemonType.Ground, MoveCategory.Physical, 50);

    private static readonly MoveDefinition Earthquake = new(
        "earthquake", "Earthquake", PokemonType.Ground, MoveCategory.Physical, 100, Target: MoveTarget.AllAdjacent);

    private static readonly MoveDefinition WaterGun = new(
        "watergun", "Water Gun", PokemonType.Water, MoveCategory.Special, 40);

    private static readonly MoveDefinition ThunderShock = new(
        "thundershock", "Thunder Shock", PokemonType.Electric, MoveCategory.Special, 40);

    private static readonly MoveDefinition Growl = new(
        "growl", "Growl", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Boosts: [Stage(BoostStat.Attack, -1)]);

    private static readonly MoveDefinition PoisonPowder = new(
        "poisonpowder", "Poison Powder", PokemonType.Poison, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Status: MajorStatus.Poison, Powder: true);

    private static readonly MoveDefinition SolarBeam = new(
        "solarbeam", "Solar Beam", PokemonType.Grass, MoveCategory.Special, 120, Charge: true);

    private static MoveDefinition Damager(int amount) =>
        new("dragonrage", "Dragon Rage", PokemonType.Dragon, MoveCategory.Special, 0, Priority: 1, FixedDamage: amount);

    private static MoveDefinition Inflict(MajorStatus status) => new(
        "inflict", "Inflict", PokemonType.Normal, MoveCategory.Status, 0, Priority: 1, Target: MoveTarget.Normal,
        Status: status);

    private static MoveDefinition WeatherMove(Weather weather) => new(
        "weather", "Weather", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.All, SetsWeather: weather);

    private static MoveDefinition Strike(PokemonType type) =>
        new("strike", "Strike", type, MoveCategory.Special, 40);

    private static PokemonSet Holding(PokemonSet set, string item) => set with { Item = item };

    private static PokemonSet Typed(PokemonSet set, params PokemonType[] types) => set with { Types = types };

    private static IReadOnlyList<string> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return Describe(battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events);
    }

    private static BattlePokemon Active(Battle battle, SideId side, int slot = 0) => battle.GetSide(side).Active[slot]!;

    // ---- Leftovers, Black Sludge (residual order 5, sub-order 4) ----

    [Fact]
    public void Leftovers_heal_a_sixteenth_of_max_hp_at_the_end_of_the_turn()
    {
        var battle = Singles([Holding(Mon("User", 100, moves: [Splash]), "leftovers")], [Mon("Foe", 50, moves: [Damager(100)])]);
        battle.Start();

        var events = Turn(battle);

        // 400 - 100 = 300, then floor(400 / 16) = 25.
        Assert.Equal(["P1.0 User leftovers activates", "P1.0 User healed 25"], events.Where(e => e.StartsWith("P1.0 User")).TakeLast(2));
        Assert.Equal(325, Active(battle, SideId.P1).CurrentHp);
    }

    [Fact]
    public void Leftovers_do_nothing_at_full_hp()
    {
        var battle = Singles([Holding(Mon("User", 100, moves: [Splash]), "leftovers")], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("leftovers") || e.Contains("healed"));
    }

    [Theory]
    [InlineData(PokemonType.Poison, "P1.0 User healed 25", 325)] // floor(400 / 16)
    [InlineData(PokemonType.Normal, "P1.0 User hurt by blacksludge 50", 250)] // floor(400 / 8)
    public void Black_sludge_heals_poison_types_and_hurts_the_others(PokemonType type, string expected, int hp)
    {
        var user = Typed(Holding(Mon("User", 100, moves: [Splash]), "blacksludge"), type);
        var battle = Singles([user], [Mon("Foe", 50, moves: [Damager(100)])]);
        battle.Start();

        Assert.Equal(expected, Turn(battle).Last(e => e.StartsWith("P1.0 User")));
        Assert.Equal(hp, Active(battle, SideId.P1).CurrentHp);
    }

    [Fact]
    public void Leftovers_heal_after_weather_damage_and_before_poison_damage()
    {
        // Residual orders: weather 1, Leftovers 5, poison 9. The Rock-type foe ignores the sandstorm.
        var foe = Typed(Mon("Foe", 50, moves: [Inflict(MajorStatus.Poison), WeatherMove(Weather.Sandstorm)]), PokemonType.Rock);
        var battle = Singles([Holding(Mon("User", 100, moves: [Splash]), "leftovers")], [foe]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p2Move: 1);

        Assert.Equal(
            ["P1.0 User hurt by Sandstorm 25", "P1.0 User leftovers activates", "P1.0 User healed 25", "P1.0 User hurt by Poison 50"],
            events.Where(e => e.StartsWith("P1.0 User") && !e.Contains("uses")));
    }

    [Fact]
    public void Leftovers_heal_after_the_holders_own_end_of_turn_ability()
    {
        // Same Pokémon: Shed Skin (sub-order 3) before Leftovers (sub-order 4).
        var user = WithAbility(Holding(Mon("User", 100, moves: [Splash]), "leftovers"), "shedskin");
        var battle = Singles([user], [Mon("Foe", 50, moves: [Damager(100), Inflict(MajorStatus.Burn)])], new PercentRolls(0));
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p2Move: 1);

        Assert.Equal(
            ["P1.0 User shedskin activates", "P1.0 User cured of Burn", "P1.0 User leftovers activates", "P1.0 User healed 25"],
            events.Where(e => e.StartsWith("P1.0 User") && !e.Contains("uses") && !e.Contains("gets")));
    }

    [Fact]
    public void A_faster_pokemons_leftovers_heal_before_a_slower_pokemons_end_of_turn_ability()
    {
        // Showdown sorts residual handlers by order, then speed, then sub-order.
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Inflict(MajorStatus.Burn)]), "leftovers")],
            [WithAbility(Mon("Foe", 50, moves: [Damager(100)]), "shedskin")],
            new PercentRolls(0));
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(
            ["P1.0 User leftovers activates", "P1.0 User healed 25", "P2.0 Foe shedskin activates", "P2.0 Foe cured of Burn"],
            events.Where(e => e.Contains("leftovers") || e.Contains("healed") || e.Contains("shedskin") || e.Contains("cured")));
    }

    [Fact]
    public void End_of_turn_without_items_keeps_weather_before_status_damage()
    {
        var foe = Typed(Mon("Foe", 50, moves: [Inflict(MajorStatus.Poison), WeatherMove(Weather.Sandstorm)]), PokemonType.Rock);
        var battle = Singles([Mon("User", 100, moves: [Splash])], [foe]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p2Move: 1);

        Assert.Equal(
            ["P1.0 User hurt by Sandstorm 25", "P1.0 User hurt by Poison 50"],
            events.Where(e => e.StartsWith("P1.0 User") && !e.Contains("uses")));
    }

    // ---- Sticky Barb ----

    [Fact]
    public void Sticky_barb_hurts_its_holder_after_status_damage()
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Splash]), "stickybarb")], [Mon("Foe", 50, moves: [Inflict(MajorStatus.Burn)])]);
        battle.Start();

        var events = Turn(battle);

        // Burn (order 10) floor(400 / 8) = 50, then Sticky Barb (order 28) floor(400 / 8) = 50.
        Assert.Equal(["P1.0 User hurt by Burn 50", "P1.0 User hurt by stickybarb 50"], events.Where(e => e.Contains("hurt by")));
        Assert.Equal(300, Active(battle, SideId.P1).CurrentHp);
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)] // no contact
    [InlineData(true, "leftovers", false)] // the attacker already holds an item
    public void Sticky_barb_moves_to_a_contact_attacker_without_an_item(bool contact, string? attackerItem, bool moves)
    {
        var user = Mon("User", 100, moves: [contact ? ContactTackle : Tackle]) with { Item = attackerItem };
        var battle = Singles([user], [Holding(Mon("Foe", 50, moves: [Splash]), "stickybarb")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(moves ? null : "stickybarb", Active(battle, SideId.P2).Item);
        Assert.Equal(moves ? "stickybarb" : attackerItem, Active(battle, SideId.P1).Item);
        // The new holder is hurt at the end of the same turn; the old one is not.
        Assert.Equal(moves, events.Contains("P1.0 User hurt by stickybarb 50"));
        Assert.Equal(!moves, events.Contains("P2.0 Foe hurt by stickybarb 50"));
    }

    // ---- Toxic Orb, Flame Orb (residual order 28) ----

    [Theory]
    [InlineData("toxicorb", MajorStatus.Toxic, 25)] // 1/16 on the first toxic turn
    [InlineData("flameorb", MajorStatus.Burn, 50)] // 1/8
    public void Status_orbs_afflict_their_holder_at_the_end_of_the_turn(string item, MajorStatus status, int nextDamage)
    {
        var battle = Singles([Holding(Mon("User", 100, moves: [Splash]), item)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var first = Turn(battle);
        Assert.Equal([$"P1.0 User {item} activates", $"P1.0 User gets {status}"], first.Where(e => e.StartsWith("P1.0 User") && !e.Contains("uses")));

        var second = Turn(battle);
        Assert.Contains($"P1.0 User hurt by {status} {nextDamage}", second);
    }

    [Theory]
    [InlineData("flameorb", PokemonType.Fire, null)]
    [InlineData("toxicorb", PokemonType.Poison, null)]
    [InlineData("toxicorb", PokemonType.Steel, null)]
    [InlineData("toxicorb", PokemonType.Normal, "immunity")]
    [InlineData("flameorb", PokemonType.Normal, "waterveil")]
    public void Status_orbs_respect_type_and_ability_immunities_silently(string item, PokemonType type, string? ability)
    {
        var user = Typed(Holding(Mon("User", 100, moves: [Splash]), item), type) with { Ability = ability };
        var battle = Singles([user], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Null(Active(battle, SideId.P1).Status);
        Assert.DoesNotContain(events, e => e.StartsWith("P1.0 User") && !e.Contains("uses"));
    }

    [Fact]
    public void Status_orbs_do_not_replace_an_existing_status()
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Splash]), "toxicorb")], [Mon("Foe", 50, moves: [Inflict(MajorStatus.Paralysis)])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(MajorStatus.Paralysis, Active(battle, SideId.P1).Status);
        Assert.DoesNotContain(events, e => e.Contains("toxicorb"));
    }

    // ---- Focus Sash, Focus Band ----

    [Fact]
    public void Focus_sash_survives_a_knock_out_from_full_hp_and_is_consumed()
    {
        var battle = Singles([Mon("User", 100, moves: [Damager(999)])], [Holding(Mon("Foe", 50, moves: [Splash]), "focussash"), Mon("Spare", 10)]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P2.0 Foe focussash consumed", "P2.0 Foe damaged"], events.Where(e => e.StartsWith("P2.0 Foe") && !e.Contains("uses")));
        Assert.Equal((1, (string?)null), (Active(battle, SideId.P2).CurrentHp, Active(battle, SideId.P2).Item));
        Assert.Contains("P2.0 Foe fainted", Turn(battle));
    }

    [Fact]
    public void Focus_sash_does_nothing_below_full_hp()
    {
        var battle = Singles(
            [Mon("User", 100, moves: [Damager(1), Damager(999)])], [Holding(Mon("Foe", 50, moves: [Splash]), "focussash"), Mon("Spare", 10)]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p1Move: 1);

        Assert.Contains("P2.0 Foe fainted", events);
        Assert.DoesNotContain(events, e => e.Contains("focussash"));
    }

    [Fact]
    public void Sturdy_acts_before_focus_sash_which_is_kept()
    {
        // Sturdy's onDamage has priority -30, Focus Sash's -40: Sturdy leaves 1 HP first.
        var foe = WithAbility(Holding(Mon("Foe", 50, moves: [Splash]), "focussash"), "sturdy");
        var battle = Singles([Mon("User", 100, moves: [Damager(999)])], [foe, Mon("Spare", 10)]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Foe sturdy activates", events);
        Assert.DoesNotContain(events, e => e.Contains("focussash"));
        Assert.Equal((1, "focussash"), (Active(battle, SideId.P2).CurrentHp, Active(battle, SideId.P2).Item));
    }

    [Theory]
    [InlineData(0, true)] // 1 in 10
    [InlineData(1, false)]
    public void Focus_band_may_leave_its_holder_at_1_hp_without_being_consumed(int roll, bool survives)
    {
        var battle = Singles(
            [Mon("User", 100, moves: [Damager(999)])], [Holding(Mon("Foe", 50, moves: [Splash]), "focusband"), Mon("Spare", 10)],
            new PercentRolls().Script(0, 10, roll));
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(survives, events.Contains("P2.0 Foe focusband activates"));
        Assert.Equal(survives, !events.Contains("P2.0 Foe fainted"));
        if (survives)
        {
            Assert.Equal((1, "focusband"), (Active(battle, SideId.P2).CurrentHp, Active(battle, SideId.P2).Item));
        }
    }

    // ---- Shell Bell ----

    [Fact]
    public void Shell_bell_heals_an_eighth_of_the_damage_dealt()
    {
        var battle = Singles([Holding(Mon("User", 50, moves: [Tackle]), "shellbell")], [Mon("Foe", 100, moves: [Damager(100)])]);
        battle.Start();

        var events = Turn(battle);

        // 300 after Dragon Rage; Tackle deals 36, floor(36 / 8) = 4.
        Assert.Equal(["P1.0 User shellbell activates", "P1.0 User healed 4"], events.SkipWhile(e => e != "P2.0 Foe damaged").Skip(1).Take(2));
        Assert.Equal(304, Active(battle, SideId.P1).CurrentHp);
    }

    [Fact]
    public void Shell_bell_does_not_heal_when_sheer_force_removes_the_secondary_effects()
    {
        var move = Tackle with { Secondaries = [new SecondaryEffect(10, Status: MajorStatus.Paralysis)] };
        var user = WithAbility(Holding(Mon("User", 50, moves: [move]), "shellbell"), "sheerforce");
        var battle = Singles([user], [Mon("Foe", 100, moves: [Damager(100)])]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("shellbell") || e.Contains("healed"));
    }

    [Fact]
    public void Shell_bell_counts_the_damage_dealt_to_every_target()
    {
        var user = Holding(Mon("User", 50, moves: [Fixtures.Surf]), "shellbell");
        var battle = Doubles(
            [user, Mon("Ally", 40, moves: [Splash])],
            [Mon("Left", 100, moves: [Damager(100)]), Mon("Right", 90, moves: [Splash])]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0)));

        var events = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0))).Events;

        var dealt = events.OfType<DamageDealt>().Where(d => d.Side == SideId.P2).Sum(d => d.Amount);
        Assert.Equal(dealt / 8, events.OfType<Healed>().Single(h => h.Side == SideId.P1).Amount);
    }

    // ---- Air Balloon ----

    [Fact]
    public void Air_balloon_is_announced_on_switch_in()
    {
        var battle = Singles([Mon("User", 100, moves: [Splash])], [Holding(Mon("Foe", 50, moves: [Splash]), "airballoon")]);

        Assert.Contains("P2.0 Foe airballoon activates", Describe(battle.Start()));
    }

    [Fact]
    public void Air_balloon_makes_its_holder_immune_to_ground_moves()
    {
        var battle = Singles([Mon("User", 100, moves: [GroundStrike])], [Holding(Mon("Foe", 50, moves: [Splash]), "airballoon")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Foe immune", events);
        Assert.Equal((400, "airballoon"), (Active(battle, SideId.P2).CurrentHp, Active(battle, SideId.P2).Item));
    }

    [Fact]
    public void Air_balloon_pops_when_its_holder_is_hit_by_a_damaging_move()
    {
        var battle = Singles([Mon("User", 100, moves: [Tackle, GroundStrike])], [Holding(Mon("Foe", 50, moves: [Splash]), "airballoon")]);
        battle.Start();

        var hit = Turn(battle);
        Assert.Equal(["P2.0 Foe damaged", "P2.0 Foe airballoon consumed"], hit.Where(e => e.StartsWith("P2.0 Foe") && !e.Contains("uses")));
        Assert.Null(Active(battle, SideId.P2).Item);

        Assert.Contains("P2.0 Foe damaged", Turn(battle, p1Move: 1));
    }

    [Fact]
    public void Air_balloon_keeps_its_holder_out_of_arena_trap()
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Splash]), "airballoon"), Mon("Bench", 10)],
            [WithAbility(Mon("Trapper", 50, moves: [Splash]), "arenatrap")]);
        battle.Start();

        Assert.Empty(battle.PendingRequests.Single(r => r.Side == SideId.P1).TrappedSlots);
    }

    [Fact]
    public void Air_balloon_spares_its_holder_from_a_spread_ground_move_in_doubles()
    {
        var battle = Doubles(
            [Mon("User", 100, moves: [Earthquake]), Mon("Ally", 40, moves: [Splash])],
            [Holding(Mon("Left", 50, moves: [Splash]), "airballoon"), Mon("Right", 45, moves: [Splash])]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0)));

        var events = Describe(battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0, 0))).Events);

        Assert.Contains("P2.0 Left immune", events);
        Assert.Contains("P2.1 Right damaged", events);
        Assert.Contains("P1.1 Ally damaged", events);
    }

    // ---- Rocky Helmet ----

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)] // no contact
    [InlineData(true, "magicguard", false)]
    public void Rocky_helmet_hurts_a_contact_attacker_by_a_sixth_of_its_max_hp(bool contact, string? ability, bool hurts)
    {
        var user = Mon("User", 100, moves: [contact ? ContactTackle : Tackle]) with { Ability = ability };
        var battle = Singles([user], [Holding(Mon("Foe", 50, moves: [Splash]), "rockyhelmet")]);
        battle.Start();

        var events = Turn(battle);

        // floor(400 / 6) = 66.
        Assert.Equal(hurts, events.Contains("P1.0 User hurt by rockyhelmet 66"));
        Assert.Equal(hurts ? 334 : 400, Active(battle, SideId.P1).CurrentHp);
    }

    [Fact]
    public void Rocky_helmet_acts_before_contact_abilities_and_after_its_holder_faints()
    {
        // DamagingHit: Rocky Helmet has order 2, Static none (last); the KO'd holder still acts.
        var foe = WithAbility(Holding(Mon("Foe", 50, hp: 30, moves: [Splash]), "rockyhelmet"), "static");
        var battle = Singles([Mon("User", 100, moves: [ContactTackle])], [foe, Mon("Spare", 10)], new PercentRolls().Script(0, 10, 0));
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(
            ["P2.0 Foe fainted", "P1.0 User hurt by rockyhelmet 66", "P2.0 Foe static activates", "P1.0 User gets Paralysis"],
            events.Where(e => e.Contains("fainted") || e.Contains("rockyhelmet") || e.Contains("static") || e.Contains("gets")));
    }

    // ---- Weakness Policy and single-use type reactions ----

    [Fact]
    public void Weakness_policy_raises_attack_and_special_attack_by_two_after_a_super_effective_hit()
    {
        var foe = Typed(Holding(Mon("Foe", 50, moves: [Splash]), "weaknesspolicy"), PokemonType.Fire);
        var battle = Singles([Mon("User", 100, moves: [WaterGun])], [foe]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(
            ["P2.0 Foe damaged", "P2.0 Foe weaknesspolicy consumed", "P2.0 Foe Attack +2 = 2", "P2.0 Foe SpecialAttack +2 = 2"],
            events.Where(e => e.StartsWith("P2.0 Foe") && !e.Contains("uses") && !e.Contains("effectiveness")));
        Assert.Null(Active(battle, SideId.P2).Item);
    }

    [Theory]
    [InlineData("neutral")]
    [InlineData("fixed")] // fixed-damage moves never trigger it (move.damage)
    public void Weakness_policy_ignores_neutral_and_fixed_damage_hits(string scenario)
    {
        var move = scenario == "neutral" ? Tackle : WaterGun with { BasePower = 0, FixedDamage = 20 };
        var foe = Typed(Holding(Mon("Foe", 50, moves: [Splash]), "weaknesspolicy"), PokemonType.Fire);
        var battle = Singles([Mon("User", 100, moves: [move])], [foe]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Foe damaged", events);
        Assert.Equal("weaknesspolicy", Active(battle, SideId.P2).Item);
    }

    [Theory]
    [InlineData("absorbbulb", PokemonType.Water, BoostStat.SpecialAttack)]
    [InlineData("cellbattery", PokemonType.Electric, BoostStat.Attack)]
    [InlineData("luminousmoss", PokemonType.Water, BoostStat.SpecialDefense)]
    [InlineData("snowball", PokemonType.Ice, BoostStat.Attack)]
    public void Type_reaction_items_raise_a_stat_once_when_hit_by_their_type(string item, PokemonType type, BoostStat stat)
    {
        var battle = Singles([Mon("User", 100, moves: [Strike(type), Tackle])], [Holding(Mon("Foe", 50, moves: [Splash]), item)]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle, p1Move: 1), e => e.Contains(item));
        var events = Turn(battle);
        Assert.Equal([$"P2.0 Foe {item} consumed", $"P2.0 Foe {stat} +1 = 1"], events.SkipWhile(e => e != "P2.0 Foe damaged").Skip(1).Take(2));

        Turn(battle);
        Assert.Equal(1, Active(battle, SideId.P2).GetStage(stat));
    }

    // ---- White Herb ----

    [Fact]
    public void White_herb_restores_lowered_stages_after_the_move()
    {
        var battle = Singles([Holding(Mon("User", 100, moves: [Splash]), "whiteherb")], [Mon("Foe", 50, moves: [Growl])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(
            ["P1.0 User Attack -1 = -1", "P1.0 User whiteherb consumed", "P1.0 User negative stages cleared"],
            events.Where(e => e.StartsWith("P1.0 User") && !e.Contains("uses")));
        Assert.Equal(0, Active(battle, SideId.P1).GetStage(BoostStat.Attack));
        Assert.Null(Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void White_herb_restores_a_drop_from_intimidate_on_switch_in()
    {
        var battle = Singles([Holding(Mon("User", 100, moves: [Splash]), "whiteherb")], [WithAbility(Mon("Foe", 50, moves: [Splash]), "intimidate")]);

        var events = Describe(battle.Start());

        Assert.Contains("P1.0 User whiteherb consumed", events);
        Assert.Equal(0, Active(battle, SideId.P1).GetStage(BoostStat.Attack));
    }

    [Fact]
    public void White_herb_keeps_raised_stages()
    {
        var shift = new MoveDefinition("shift", "Shift", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self,
            Boosts: [Stage(BoostStat.Attack, 1), Stage(BoostStat.Defense, -1)]);
        var battle = Singles([Holding(Mon("User", 100, moves: [shift]), "whiteherb")], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Turn(battle);

        var user = Active(battle, SideId.P1);
        Assert.Equal((1, 0), (user.GetStage(BoostStat.Attack), user.GetStage(BoostStat.Defense)));
    }

    // ---- Power Herb ----

    [Fact]
    public void Power_herb_lets_a_two_turn_move_attack_at_once_and_is_consumed()
    {
        var battle = Singles([Holding(Mon("User", 100, moves: [SolarBeam]), "powerherb")], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(
            ["P1.0 User uses solarbeam", "P1.0 User charges solarbeam", "P1.0 User powerherb consumed", "P2.0 Foe damaged"],
            events.Take(4));
        Assert.Contains(battle.PendingRequests, r => r.Side == SideId.P1 && r.Kind == DecisionKind.Turn);
        Assert.Null(Active(battle, SideId.P1).Item);
    }

    // ---- Berry Juice ----

    [Theory]
    [InlineData(200, true)] // 200 <= 400 / 2
    [InlineData(199, false)]
    public void Berry_juice_heals_20_hp_once_its_holder_is_at_half_hp_or_less(int damage, bool heals)
    {
        var battle = Singles([Holding(Mon("User", 50, moves: [Splash]), "berryjuice")], [Mon("Foe", 100, moves: [Damager(damage)])]);
        battle.Start();

        var events = Turn(battle);

        if (heals)
        {
            Assert.Equal(["P1.0 User damaged", "P1.0 User berryjuice consumed", "P1.0 User healed 20"],
                events.Where(e => e.StartsWith("P1.0 User") && !e.Contains("uses")));
        }

        Assert.Equal(heals ? 220 : 201, Active(battle, SideId.P1).CurrentHp);
    }

    [Fact]
    public void Berry_juice_also_acts_after_end_of_turn_damage()
    {
        var battle = Singles(
            [Holding(Mon("User", 50, moves: [Splash]), "berryjuice")], [Mon("Foe", 100, moves: [Damager(150), Inflict(MajorStatus.Burn)])]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p2Move: 1);

        // 250 - 50 burn = 200, then +20.
        Assert.Equal(["P1.0 User hurt by Burn 50", "P1.0 User berryjuice consumed", "P1.0 User healed 20"], events.Where(e => e.StartsWith("P1.0 User") && !e.Contains("uses") && !e.Contains("gets")));
        Assert.Equal(220, Active(battle, SideId.P1).CurrentHp);
    }

    // ---- Safety Goggles ----

    [Fact]
    public void Safety_goggles_block_powder_moves()
    {
        var battle = Singles([Mon("User", 100, moves: [PoisonPowder])], [Holding(Mon("Foe", 50, moves: [Splash]), "safetygoggles")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Foe safetygoggles activates", events);
        Assert.Null(Active(battle, SideId.P2).Status);
    }

    [Fact]
    public void Safety_goggles_block_sandstorm_damage()
    {
        var battle = Singles([Mon("User", 100, moves: [WeatherMove(Weather.Sandstorm)])], [Holding(Mon("Foe", 50, moves: [Splash]), "safetygoggles")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P1.0 User hurt by Sandstorm 25", events);
        Assert.DoesNotContain(events, e => e.StartsWith("P2.0 Foe hurt"));
    }

    [Fact]
    public void Safety_goggles_protect_a_contact_attacker_from_effect_spore()
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [ContactTackle]), "safetygoggles")], [WithAbility(Mon("Foe", 50, moves: [Splash]), "effectspore")],
            new PercentRolls(0));
        battle.Start();

        Turn(battle);

        Assert.Null(Active(battle, SideId.P1).Status);
    }

    // ---- Weather rocks ----

    [Theory]
    [InlineData("heatrock", Weather.Sun, 8)]
    [InlineData("damprock", Weather.Rain, 8)]
    [InlineData("smoothrock", Weather.Sandstorm, 8)]
    [InlineData("icyrock", Weather.Hail, 8)]
    [InlineData("heatrock", Weather.Rain, 5)] // another weather's rock
    public void Weather_rocks_make_their_holders_weather_last_eight_turns(string item, Weather weather, int duration)
    {
        var battle = Singles([Holding(Mon("User", 100, moves: [WeatherMove(weather), Splash]), item)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var turns = 1;
        var events = Turn(battle);
        while (!events.Contains($"weather {weather} ends"))
        {
            turns++;
            events = Turn(battle, p1Move: 1);
        }

        Assert.Equal(duration, turns);
    }

    [Fact]
    public void A_weather_rock_also_extends_weather_started_by_an_ability()
    {
        // Gen 6: setWeather calls the durationCallback with the ability holder as source.
        var user = WithAbility(Holding(Mon("User", 100, moves: [Splash]), "heatrock"), "drought");
        var battle = Singles([user], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        var turns = 1;
        while (!Turn(battle).Contains("weather Sun ends"))
        {
            turns++;
        }

        Assert.Equal(8, turns);
    }

    // ---- Ring Target ----

    [Theory]
    [InlineData(PokemonType.Normal, new[] { PokemonType.Ghost }, 1.0)]
    [InlineData(PokemonType.Electric, new[] { PokemonType.Ground }, 1.0)]
    [InlineData(PokemonType.Electric, new[] { PokemonType.Ground, PokemonType.Water }, 2.0)] // the immune type counts as neutral
    public void Ring_target_removes_type_immunities(PokemonType moveType, PokemonType[] types, double effectiveness)
    {
        var move = new MoveDefinition("strike", "Strike", moveType, MoveCategory.Physical, 50);
        var foe = Typed(Holding(Mon("Foe", 50, moves: [Splash]), "ringtarget"), types);
        var battle = Singles([Mon("User", 100, moves: [move])], [foe]);
        battle.Start();

        var events = Turn(battle);

        Assert.Contains("P2.0 Foe damaged", events);
        Assert.Equal(effectiveness != 1, events.Contains($"P2.0 effectiveness x{effectiveness}"));
    }

    // ---- Magic Guard ----

    [Theory]
    [InlineData("stickybarb")]
    [InlineData("blacksludge")]
    public void Magic_guard_blocks_damage_from_its_holders_own_item(string item)
    {
        var user = WithAbility(Holding(Mon("User", 100, moves: [Splash]), item), "magicguard");
        var battle = Singles([user], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("hurt by"));
        Assert.Equal(400, Active(battle, SideId.P1).CurrentHp);
    }
}
