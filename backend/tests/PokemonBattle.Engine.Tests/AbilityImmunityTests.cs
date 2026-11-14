using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Immunity, absorb and prevention abilities, checked against Showdown's data/abilities.ts with the
/// Gen 6 mods (Soundproof from gen7, Magic Guard from gen6) and the Gen 6 hit step order.
/// </summary>
public class AbilityImmunityTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition Earthquake = new(
        "earthquake", "Earthquake", PokemonType.Ground, MoveCategory.Physical, 100, Target: MoveTarget.AllAdjacent);

    private static readonly MoveDefinition SandAttack = new(
        "sandattack", "Sand Attack", PokemonType.Ground, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Boosts: [Stage(BoostStat.Accuracy, -1)]);

    private static readonly MoveDefinition Thunderbolt = new(
        "thunderbolt", "Thunderbolt", PokemonType.Electric, MoveCategory.Special, 90);

    private static readonly MoveDefinition ThunderWave = new(
        "thunderwave", "Thunder Wave", PokemonType.Electric, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Status: MajorStatus.Paralysis, IgnoreImmunity: false);

    private static readonly MoveDefinition Thunder = new(
        "thunder", "Thunder", PokemonType.Electric, MoveCategory.Special, 110, Accuracy: 70);

    private static readonly MoveDefinition WaterGun = new(
        "watergun", "Water Gun", PokemonType.Water, MoveCategory.Special, 40);

    private static readonly MoveDefinition Ember = new("ember", "Ember", PokemonType.Fire, MoveCategory.Special, 40);

    private static readonly MoveDefinition Discharge = new(
        "discharge", "Discharge", PokemonType.Electric, MoveCategory.Special, 80, Target: MoveTarget.AllAdjacent);

    private static readonly MoveDefinition HyperVoice = new(
        "hypervoice", "Hyper Voice", PokemonType.Normal, MoveCategory.Special, 90, Target: MoveTarget.AllAdjacentFoes,
        Flags: MoveFlags.Sound);

    private static readonly MoveDefinition Spore = new(
        "spore", "Spore", PokemonType.Grass, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Status: MajorStatus.Sleep, Powder: true);

    private static readonly MoveDefinition Guillotine = new(
        "guillotine", "Guillotine", PokemonType.Normal, MoveCategory.Physical, 0, Accuracy: 30, Ohko: true);

    private static readonly MoveDefinition Growl = new(
        "growl", "Growl", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.AllAdjacentFoes,
        Boosts: [Stage(BoostStat.Attack, -1)], Flags: MoveFlags.Sound);

    private static readonly MoveDefinition Leer = new(
        "leer", "Leer", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.AllAdjacentFoes,
        Boosts: [Stage(BoostStat.Defense, -1)]);

    private static readonly MoveDefinition Psychic = new(
        "psychic", "Psychic", PokemonType.Psychic, MoveCategory.Special, 90,
        Secondaries: [new SecondaryEffect(100, TargetBoosts: [Stage(BoostStat.SpecialDefense, -1)])]);

    private static readonly MoveDefinition CloseCombat = new(
        "closecombat", "Close Combat", PokemonType.Fighting, MoveCategory.Physical, 120,
        SelfBoosts: [Stage(BoostStat.Defense, -1), Stage(BoostStat.SpecialDefense, -1)]);

    private static readonly MoveDefinition Spark = new(
        "spark", "Spark", PokemonType.Electric, MoveCategory.Physical, 65,
        Secondaries: [new SecondaryEffect(30, Status: MajorStatus.Paralysis)]);

    private static readonly MoveDefinition FlameCharge = new(
        "flamecharge", "Flame Charge", PokemonType.Fire, MoveCategory.Physical, 50,
        Secondaries: [new SecondaryEffect(100, SelfBoosts: [Stage(BoostStat.Speed, 1)])]);

    private static readonly MoveDefinition Explosion = new(
        "explosion", "Explosion", PokemonType.Normal, MoveCategory.Physical, 250, Target: MoveTarget.AllAdjacent,
        SelfDestruct: SelfDestruct.Always);

    private static MoveDefinition Weather(Weather weather) => new(
        "weather", "Weather", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.All, SetsWeather: weather);

    private static MoveDefinition Inflict(MajorStatus status) => new(
        "inflict", "Inflict", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Normal, Status: status);

    private static MoveDefinition Damager(int amount) =>
        new("dragonrage", "Dragon Rage", PokemonType.Dragon, MoveCategory.Special, 0, Priority: 1, FixedDamage: amount);

    private static PokemonSet Foe(string ability, params MoveDefinition[] moves) =>
        WithAbility(Mon("Foe", 50, moves: moves.Length == 0 ? [Splash] : moves), ability);

    private static PokemonSet MoldBreaker(PokemonSet set) => WithAbility(set, "moldbreaker", breakable: false);

    private static IReadOnlyList<string> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return Describe(battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events);
    }

    private static BattlePokemon P2(Battle battle, int slot = 0) => battle.GetSide(SideId.P2).Active[slot]!;

    [Fact]
    public void Levitate_dodges_ground_moves_but_not_ground_status_moves()
    {
        var battle = Singles([Mon("User", 100, moves: [Earthquake, SandAttack])], [Foe("levitate")]);
        battle.Start();

        var quake = Turn(battle);
        var sand = Turn(battle, p1Move: 1);

        Assert.Equal(["P1.0 User uses earthquake", "P2.0 Foe immune (levitate)"], quake.Take(2));
        Assert.Contains("P2.0 Foe Accuracy -1 = -1", sand);
    }

    [Theory]
    [InlineData("voltabsorb", "thunderbolt")]
    [InlineData("voltabsorb", "thunderwave")]
    [InlineData("waterabsorb", "watergun")]
    [InlineData("dryskin", "watergun")]
    public void Absorbing_abilities_heal_a_quarter_or_are_immune_at_full_hp(string ability, string moveId)
    {
        var move = new[] { Thunderbolt, ThunderWave, WaterGun }.Single(m => m.Id == moveId);
        var battle = Singles([Mon("User", 100, moves: [move, Damager(200)])], [Foe(ability)]);
        battle.Start();

        var atFullHp = Turn(battle);
        Turn(battle, p1Move: 1);
        var hurt = Turn(battle);

        Assert.Equal([$"P1.0 User uses {moveId}", $"P2.0 Foe immune ({ability})"], atFullHp.Take(2));
        Assert.Equal(
            [$"P1.0 User uses {moveId}", $"P2.0 Foe {ability} activates", "P2.0 Foe healed 100"], hurt.Take(3));
        Assert.Null(P2(battle).Status);
    }

    [Fact]
    public void Absorbed_moves_do_not_roll_accuracy()
    {
        var battle = Singles([Mon("User", 100, moves: [Thunder])], [Foe("voltabsorb")], new PercentRolls());
        battle.Start();

        Assert.Contains("P2.0 Foe immune (voltabsorb)", Turn(battle));
    }

    [Fact]
    public void Dry_skin_takes_more_from_fire_moves()
    {
        var user = Mon("User", 100, moves: [Ember]);
        var foe = Foe("dryskin");
        var battle = Singles([user], [foe]);
        battle.Start();

        battle.Submit(SideId.P1, Move());
        var dealt = battle.Submit(SideId.P2, Move()).Events.OfType<DamageDealt>().Single(d => d.Side == SideId.P2).Amount;

        // Ember's 40 power x1.25 (5120/4096) = 50 (Showdown's onSourceBasePower).
        var expected = new DamageCalculator(TestTypeChart.Gen6Sample)
            .Calculate(user, foe, Ember with { Id = "fixed", BasePower = 50 }, false, new MaxRandom()).Damage;
        Assert.Equal(expected, dealt);
    }

    [Fact]
    public void Dry_skin_heals_in_rain_and_hurts_in_sun()
    {
        var battle = Singles(
            [Mon("User", 100, moves: [Damager(200), Weather(Battles.Weather.Rain), Weather(Battles.Weather.Sun)])],
            [Foe("dryskin")]);
        battle.Start();
        Turn(battle);

        var rain = Turn(battle, p1Move: 1);
        var sun = Turn(battle, p1Move: 2);

        Assert.Equal(["P2.0 Foe dryskin activates", "P2.0 Foe healed 50"], rain.SkipWhile(e => e != "weather Rain continues").Skip(1).Take(2));
        Assert.Contains("P2.0 Foe hurt by Sun 50", sun);
    }

    [Fact]
    public void Flash_fire_absorbs_fire_moves_and_powers_up_its_own()
    {
        var battle = Singles([Mon("User", 100, moves: [Ember])], [Foe("flashfire", Ember)]);
        battle.Start();

        battle.Submit(SideId.P1, Move());
        var first = battle.Submit(SideId.P2, Move()).Events;
        var second = Turn(battle);

        Assert.Contains("P2.0 Foe flashfire activates", Describe(first));
        // Special Attack 150 against 100 with a 40 power Fire move: floor(floor(22 * 40 * 150 / 100) / 50) + 2 = 28.
        Assert.Equal(28, first.OfType<DamageDealt>().Single(d => d.Side == SideId.P1).Amount);
        Assert.Contains("P2.0 Foe immune (flashfire)", second);
    }

    [Fact]
    public void Lightning_rod_absorbs_electric_moves_to_raise_special_attack()
    {
        var battle = Singles([Mon("User", 100, moves: [Thunderbolt])], [Foe("lightningrod")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P2.0 Foe lightningrod activates", "P2.0 Foe SpecialAttack +1 = 1"], events.Skip(1).Take(2));
        Assert.DoesNotContain(events, e => e.StartsWith("P2.0 Foe damaged"));
    }

    [Fact]
    public void Lightning_rod_draws_single_target_electric_moves_in_doubles()
    {
        var battle = Doubles(
            [Mon("User", 100, moves: [Thunderbolt]), Mon("Ally", 90, moves: [Splash])],
            [Mon("Target", 50, moves: [Splash]), Foe("lightningrod")]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0)));

        var events = Describe(battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0))).Events);

        Assert.Contains("P2.1 Foe SpecialAttack +1 = 1", events);
        Assert.DoesNotContain(events, e => e.StartsWith("P2.0 Target damaged"));
    }

    [Fact]
    public void Lightning_rod_also_draws_an_allys_electric_move()
    {
        var battle = Doubles(
            [Mon("User", 100, moves: [Thunderbolt]), WithAbility(Mon("Rod", 90, moves: [Splash]), "lightningrod")],
            [Mon("Target", 50, moves: [Splash]), Mon("Other", 40, moves: [Splash])]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0)));

        var events = Describe(battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0))).Events);

        Assert.Contains("P1.1 Rod SpecialAttack +1 = 1", events);
        Assert.DoesNotContain(events, e => e.StartsWith("P2.0 Target damaged"));
    }

    [Fact]
    public void Spread_electric_moves_are_not_redirected()
    {
        var battle = Doubles(
            [Mon("User", 100, moves: [Discharge]), Mon("Ally", 90, moves: [Splash])],
            [Mon("Target", 50, moves: [Splash]), Foe("lightningrod")]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0)));

        var events = Describe(battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0))).Events);

        Assert.Contains("P2.0 Target damaged", events);
        Assert.Contains("P2.1 Foe SpecialAttack +1 = 1", events);
    }

    [Theory]
    [InlineData("soundproof", "hypervoice")]
    [InlineData("soundproof", "growl")]
    [InlineData("overcoat", "spore")]
    [InlineData("sturdy", "guillotine")]
    public void Some_abilities_make_their_holder_immune_to_a_kind_of_move(string ability, string moveId)
    {
        var move = new[] { HyperVoice, Growl, Spore, Guillotine }.Single(m => m.Id == moveId);
        var battle = Singles([Mon("User", 100, moves: [move])], [Foe(ability)], new PercentRolls());
        battle.Start();

        Assert.Equal($"P2.0 Foe immune ({ability})", Turn(battle)[1]);
    }

    [Fact]
    public void Overcoat_shelters_from_sandstorm_and_hail()
    {
        var battle = Singles([Mon("User", 100, moves: [Weather(Battles.Weather.Hail)])], [Foe("overcoat")]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.StartsWith("P2.0 Foe hurt"));
    }

    [Theory]
    [InlineData("immunity", MajorStatus.Poison)]
    [InlineData("immunity", MajorStatus.Toxic)]
    [InlineData("insomnia", MajorStatus.Sleep)]
    [InlineData("vitalspirit", MajorStatus.Sleep)]
    [InlineData("limber", MajorStatus.Paralysis)]
    [InlineData("waterveil", MajorStatus.Burn)]
    public void Some_abilities_prevent_a_status(string ability, MajorStatus status)
    {
        var battle = Singles([Mon("User", 100, moves: [Inflict(status)])], [Foe(ability)]);
        battle.Start();

        Assert.Equal($"P2.0 Foe immune ({ability})", Turn(battle)[1]);
        Assert.Null(P2(battle).Status);
    }

    [Fact]
    public void Status_prevention_is_silent_against_secondary_effects()
    {
        var battle = Singles([Mon("User", 100, moves: [Spark])], [Foe("limber")], new PercentRolls(0));
        battle.Start();

        var events = Turn(battle);

        Assert.DoesNotContain(events, e => e.Contains("immune") || e.Contains("gets"));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Leaf_guard_prevents_status_in_sun(bool sunny, bool prevented)
    {
        var battle = Singles(
            [Mon("User", 100, moves: [sunny ? Weather(Battles.Weather.Sun) : Splash, Inflict(MajorStatus.Burn)])],
            [Foe("leafguard")]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p1Move: 1);

        Assert.Equal(prevented, events.Contains("P2.0 Foe immune (leafguard)"));
        Assert.Equal(prevented ? null : MajorStatus.Burn, P2(battle).Status);
    }

    [Fact]
    public void Sturdy_survives_a_hit_from_full_hp_with_1_hp()
    {
        var battle = Singles([Mon("User", 100, moves: [Damager(999), Damager(50)])], [Foe("sturdy"), Mon("Spare", 10)]);
        battle.Start();

        var survived = Turn(battle);
        var hpAfterFirstHit = P2(battle).CurrentHp;
        var notFromFullHp = Turn(battle, p1Move: 1);

        Assert.Equal(["P2.0 Foe sturdy activates", "P2.0 Foe damaged"], survived.Skip(1).Take(2));
        Assert.Equal(1, hpAfterFirstHit);
        Assert.Contains("P2.0 Foe fainted", notFromFullHp);
    }

    [Theory]
    [InlineData("clearbody", "growl", true)]
    [InlineData("clearbody", "leer", true)]
    [InlineData("hypercutter", "growl", true)]
    [InlineData("hypercutter", "leer", false)]
    [InlineData("bigpecks", "leer", true)]
    [InlineData("bigpecks", "growl", false)]
    [InlineData("keeneye", "sandattack", true)]
    public void Some_abilities_prevent_stat_drops_from_other_pokemon(string ability, string moveId, bool prevented)
    {
        var move = new[] { Growl, Leer, SandAttack }.Single(m => m.Id == moveId);
        var battle = Singles([Mon("User", 100, moves: [move])], [Foe(ability)]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(prevented, events.Contains($"P2.0 Foe {ability} prevents stat drop"));
        Assert.Equal(!prevented, events.Any(e => e.StartsWith("P2.0 Foe") && e.Contains("-1 = -1")));
    }

    [Fact]
    public void Stat_drop_prevention_is_silent_against_secondary_effects_and_ignores_self_drops()
    {
        // The foe lowers only its own Defense, so its Special Defense shows whether Psychic's drop landed.
        var selfDrop = CloseCombat with { SelfBoosts = [Stage(BoostStat.Defense, -1)] };
        var battle = Singles(
            [Mon("User", 100, moves: [Psychic])],
            [WithAbility(Mon("Foe", 50, moves: [selfDrop]), "clearbody")]);
        battle.Start();

        var events = Turn(battle);

        Assert.DoesNotContain(events, e => e.Contains("prevents"));
        Assert.Equal(0, P2(battle).GetStage(BoostStat.SpecialDefense));
        Assert.Equal(-1, P2(battle).GetStage(BoostStat.Defense));
    }

    [Fact]
    public void Shield_dust_blocks_secondary_effects_on_its_holder_but_not_on_the_attacker()
    {
        var spark = Singles([Mon("User", 100, moves: [Spark])], [Foe("shielddust")], new PercentRolls());
        spark.Start();
        var charge = Singles([Mon("User", 100, moves: [FlameCharge])], [Foe("shielddust")]);
        charge.Start();

        Assert.DoesNotContain(Turn(spark), e => e.Contains("gets"));
        Assert.Contains("P1.0 User Speed +1 = 1", Turn(charge));
    }

    [Fact]
    public void Damp_stops_explosion_before_the_user_faints()
    {
        var battle = Singles([Mon("User", 100, moves: [Explosion])], [Foe("damp")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P1.0 User uses explosion", "P2.0 Foe damp activates", "P1.0 explosion failed"], events.Take(3));
        Assert.False(battle.GetSide(SideId.P1).Active[0]!.IsFainted);
    }

    [Fact]
    public void Explosion_faints_its_user_even_against_an_immune_target()
    {
        var battle = Singles(
            [Mon("User", 100, moves: [Explosion]), Mon("Backup", 10)],
            [Mon("Gengar", 50, moves: [Splash]) with { Types = [PokemonType.Ghost] }]);
        battle.Start();

        Assert.Equal(
            ["P1.0 User uses explosion", "P2.0 Gengar immune", "P1.0 User fainted"], Turn(battle).Take(3));
    }

    [Fact]
    public void Explosion_hits_then_its_user_faints()
    {
        var battle = Singles([Mon("User", 100, moves: [Explosion]), Mon("Backup", 10)], [Mon("Foe", 50, hp: 999)]);
        battle.Start();

        Assert.Equal(["P1.0 User uses explosion", "P2.0 Foe damaged", "P1.0 User fainted"], Turn(battle).Take(3));
    }

    [Fact]
    public void Magic_guard_only_takes_damage_from_attacks()
    {
        var battle = Singles(
            [Mon("User", 100, moves: [Weather(Battles.Weather.Sandstorm), Inflict(MajorStatus.Burn)])],
            [Foe("magicguard") with { AbilityBreakable = false }]);
        battle.Start();

        var events = new List<string>(Turn(battle));
        events.AddRange(Turn(battle, p1Move: 1));

        Assert.Equal(MajorStatus.Burn, P2(battle).Status);
        Assert.DoesNotContain(events, e => e.StartsWith("P2.0 Foe hurt"));
    }

    [Theory]
    [InlineData("levitate", "earthquake")]
    [InlineData("voltabsorb", "thunderbolt")]
    [InlineData("sturdy", "guillotine")]
    [InlineData("damp", "explosion")]
    public void Mold_breaker_ignores_breakable_abilities(string ability, string moveId)
    {
        var move = new[] { Earthquake, Thunderbolt, Guillotine, Explosion }.Single(m => m.Id == moveId);
        var battle = Singles(
            [MoldBreaker(Mon("Rampardos", 100, moves: [move])), Mon("Backup", 10)],
            [Foe(ability), Mon("Spare", 10)],
            new PercentRolls(0));
        battle.Start();

        Assert.Equal("P2.0 Foe damaged", Turn(battle)[1]);
    }

    [Fact]
    public void Mold_breaker_ignores_multiscale()
    {
        var battle = Singles([MoldBreaker(Mon("User", 100))], [Foe("multiscale")]);
        battle.Start();
        battle.Submit(SideId.P1, Move());

        var events = battle.Submit(SideId.P2, Move()).Events;

        Assert.Equal(36, events.OfType<DamageDealt>().Single(d => d.Side == SideId.P2).Amount);
    }

    [Fact]
    public void Mold_breaker_moves_are_not_redirected_by_lightning_rod()
    {
        var battle = Doubles(
            [MoldBreaker(Mon("User", 100, moves: [Thunderbolt])), Mon("Ally", 90, moves: [Splash])],
            [Mon("Target", 50, moves: [Splash]), Foe("lightningrod")]);
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0, 0), new MoveChoice(1, 0)));

        var events = Describe(battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0))).Events);

        Assert.Contains("P2.0 Target damaged", events);
    }
}
