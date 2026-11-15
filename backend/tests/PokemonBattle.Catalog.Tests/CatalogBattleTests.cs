using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Catalog.Tests;

/// <summary>End-to-end: catalog-built teams fighting with the real Gen 6 type chart.</summary>
public class CatalogBattleTests
{
    private static readonly Gen6Catalog Catalog = Gen6Catalog.Instance;
    private static readonly TeamBuilder Builder = new(Catalog);

    private static IReadOnlyList<BattleEvent> PlayFirstTurn(PokemonSetSpec p1, PokemonSetSpec p2)
    {
        var p1Team = Builder.Build([p1]);
        var p2Team = Builder.Build([p2]);
        Assert.Empty(p1Team.Errors.Concat(p2Team.Errors));

        var battle = Battle.Create(BattleFormat.Singles, p1Team.Team, p2Team.Team, Catalog.TypeChart, new SeededBattleRandom(7));
        battle.Start();
        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0)));
        return battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0))).Events;
    }

    [Fact]
    public void Electric_move_is_four_times_effective_against_gyarados()
    {
        var events = PlayFirstTurn(
            new PokemonSetSpec("Raichu", ["Thunderbolt"], "Static"),
            new PokemonSetSpec("Gyarados", ["Waterfall"], "Intimidate"));

        Assert.Contains(new Effectiveness(SideId.P2, 0, 4), events);
    }

    [Fact]
    public void Seismic_toss_deals_the_users_level_and_cannot_touch_ghosts()
    {
        var vsGyarados = PlayFirstTurn(
            new PokemonSetSpec("Machamp", ["Seismic Toss"], "Guts"),
            new PokemonSetSpec("Gyarados", ["Waterfall"], "Intimidate"));
        var vsGengar = PlayFirstTurn(
            new PokemonSetSpec("Machamp", ["Seismic Toss"], "Guts"),
            new PokemonSetSpec("Gengar", ["Shadow Ball"], "Levitate"));

        Assert.Contains(vsGyarados, e => e is DamageDealt { Side: SideId.P2, Amount: 100 });
        Assert.Contains(new Immune(SideId.P2, 0, "Gengar"), vsGengar);
    }

    [Fact]
    public void Moves_map_accuracy_and_stat_changes_from_gen_6_data()
    {
        MoveDefinition Move(string id) => Catalog.FindMove(id)!.ToDefinition();

        var swordsDance = Move("swordsdance");
        Assert.Null(swordsDance.Accuracy);
        Assert.Equal(MoveTarget.Self, swordsDance.Target);
        Assert.Equal([new StatChange(BoostStat.Attack, 2)], swordsDance.Boosts);

        Assert.Equal(70, Move("thunder").Accuracy);
        Assert.Equal([new StatChange(BoostStat.Accuracy, -1)], Move("sandattack").Boosts);
        Assert.Equal([new StatChange(BoostStat.Evasion, 1)], Move("doubleteam").Boosts);

        var closeCombat = Move("closecombat");
        Assert.Equal([new StatChange(BoostStat.Defense, -1), new StatChange(BoostStat.SpecialDefense, -1)], closeCombat.SelfBoosts);
        Assert.Null(closeCombat.SelfBoostChance);

        // Gen 6 (Showdown data/mods/gen6): a 50% secondary +1 Defense, not Gen 7's 50% self +2.
        var diamondStorm = Move("diamondstorm");
        Assert.Empty(diamondStorm.SelfBoosts);
        var diamondStormBoost = Assert.Single(diamondStorm.Secondaries);
        Assert.Equal(50, diamondStormBoost.Chance);
        Assert.Equal([new StatChange(BoostStat.Defense, 1)], diamondStormBoost.SelfBoosts);

        // Gen 6 (Showdown data/mods/gen7): Rapid Spin does not raise Speed until Gen 8.
        Assert.Empty(Move("rapidspin").Secondaries);

        var psychic = Assert.Single(Move("psychic").Secondaries);
        Assert.Equal(10, psychic.Chance);
        Assert.Equal([new StatChange(BoostStat.SpecialDefense, -1)], psychic.TargetBoosts);

        var flameCharge = Assert.Single(Move("flamecharge").Secondaries);
        Assert.Equal(100, flameCharge.Chance);
        Assert.Equal([new StatChange(BoostStat.Speed, 1)], flameCharge.SelfBoosts);

        var chipAway = Move("chipaway");
        Assert.True(chipAway.IgnoreEvasion);
        Assert.True(chipAway.IgnoreDefensive);
        Assert.False(Move("tackle").IgnoreEvasion);
    }

    [Fact]
    public void Forced_switch_moves_map_from_gen_6_data()
    {
        MoveDefinition Move(string id) => Catalog.FindMove(id)!.ToDefinition();

        foreach (var id in new[] { "roar", "whirlwind", "dragontail", "circlethrow" })
        {
            Assert.True(Move(id).ForceSwitch, id);
            Assert.Equal(-6, Move(id).Priority);
        }

        Assert.Equal(MoveCategory.Status, Move("roar").Category);
        Assert.Equal(MoveCategory.Physical, Move("dragontail").Category);
        Assert.False(Move("tackle").ForceSwitch);
        Assert.False(Move("uturn").ForceSwitch);
    }

    [Fact]
    public void Catalog_teams_eat_type_resist_berries_against_super_effective_hits()
    {
        var events = PlayFirstTurn(
            new PokemonSetSpec("Raichu", ["Thunderbolt"], "Static"),
            new PokemonSetSpec("Gyarados", ["Waterfall"], "Intimidate", Item: "Wacan Berry"));

        Assert.Contains(new ItemConsumed(SideId.P2, 0, "Gyarados", "wacanberry"), events);
    }

    [Fact]
    public void Catalog_teams_with_red_card_drag_out_the_attacker()
    {
        var p1 = Builder.Build([new PokemonSetSpec("Machamp", ["Close Combat"], "Guts"), new PokemonSetSpec("Pidgey", ["Tackle"], "Keen Eye")]);
        var p2 = Builder.Build([new PokemonSetSpec("Snorlax", ["Rest"], "Thick Fat", Item: "Red Card")]);
        Assert.Empty(p1.Errors.Concat(p2.Errors));
        var battle = Battle.Create(BattleFormat.Singles, p1.Team, p2.Team, Catalog.TypeChart, new SeededBattleRandom(7));
        battle.Start();

        battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0)));
        var events = battle.Submit(SideId.P2, new Decision(new MoveChoice(0, 0))).Events;

        Assert.Contains(new ItemConsumed(SideId.P2, 0, "Snorlax", "redcard"), events);
        Assert.Contains(new ForcedOut(SideId.P1, 0, "Machamp"), events);
        Assert.Equal("Pidgey", battle.GetSide(SideId.P1).Active[0]!.Set.Species);
    }

    [Fact]
    public void Moves_map_status_effects_from_gen_6_data()
    {
        MoveDefinition Move(string id) => Catalog.FindMove(id)!.ToDefinition();

        var thunderWave = Move("thunderwave");
        Assert.Equal(MajorStatus.Paralysis, thunderWave.Status);
        Assert.False(thunderWave.IgnoresTypeImmunity);
        Assert.True(Move("glare").IgnoresTypeImmunity);
        Assert.False(Move("tackle").IgnoresTypeImmunity);

        Assert.Equal(MajorStatus.Toxic, Move("toxic").Status);
        Assert.Equal(MajorStatus.Sleep, Move("spore").Status);
        Assert.True(Move("spore").Powder);
        Assert.Equal(MajorStatus.Burn, Move("willowisp").Status);

        var thunderbolt = Assert.Single(Move("thunderbolt").Secondaries);
        Assert.Equal(10, thunderbolt.Chance);
        Assert.Equal(MajorStatus.Paralysis, thunderbolt.Status);

        // Fire Fang's flinch secondary waits for volatile conditions; the burn is kept.
        var fireFang = Assert.Single(Move("firefang").Secondaries);
        Assert.Equal(MajorStatus.Burn, fireFang.Status);

        Assert.True(Move("flamewheel").Defrost);
        Assert.True(Move("scald").ThawsTarget);
        Assert.True(Move("snore").SleepUsable);

        // Gen 6: every OHKO move (Sheer Cold included) is a plain OHKO; Ice immunity arrives in Gen 7.
        Assert.True(Move("fissure").Ohko);
        Assert.True(Move("sheercold").Ohko);
        Assert.False(Move("earthquake").Ohko);

        Assert.Equal(Weather.Sun, Move("sunnyday").SetsWeather);
        Assert.Equal(Weather.Rain, Move("raindance").SetsWeather);
        Assert.Equal(Weather.Sandstorm, Move("sandstorm").SetsWeather);
        Assert.Equal(Weather.Hail, Move("hail").SetsWeather);
        Assert.Null(Move("tackle").SetsWeather);
        Assert.Equal(new HealFraction(1, 2), Move("recover").Heal);
        Assert.Null(Move("tackle").Heal);

        Assert.True(Move("solarbeam").Charge);
        Assert.True(Move("fly").Charge);
        Assert.True(Move("skullbash").Charge);
        Assert.False(Move("tackle").Charge);
        Assert.True(Move("hyperbeam").Recharge);
        Assert.True(Move("gigaimpact").Recharge);
        Assert.False(Move("closecombat").Recharge);

        Assert.Equal(MoveFlags.Contact | MoveFlags.Punch, Move("firepunch").Flags & (MoveFlags.Contact | MoveFlags.Punch));
        Assert.True(Move("aurasphere").Flags.HasFlag(MoveFlags.Pulse));
        Assert.False(Move("thunderbolt").Flags.HasFlag(MoveFlags.Contact));
        Assert.True(Move("doubleedge").HasRecoil);
        Assert.True(Move("highjumpkick").HasRecoil); // crash damage counts for Reckless
        Assert.False(Move("tackle").HasRecoil);

        Assert.Equal(SelfDestruct.Always, Move("explosion").SelfDestruct);
        Assert.Equal(SelfDestruct.Always, Move("selfdestruct").SelfDestruct);
        Assert.Equal(SelfDestruct.IfHit, Move("finalgambit").SelfDestruct);
        Assert.Equal(SelfDestruct.IfHit, Move("memento").SelfDestruct);
        Assert.Null(Move("tackle").SelfDestruct);

        // Sheer Force counts every secondary, including the ones the engine does not model yet.
        Assert.True(Move("bite").HasSecondaryEffects); // flinch only
        Assert.True(Move("triattack").HasSecondaryEffects); // random status from a handler
        Assert.True(Move("thunderbolt").HasSecondaryEffects);
        Assert.False(Move("closecombat").HasSecondaryEffects); // only self drops
        Assert.False(Move("tackle").HasSecondaryEffects);
    }

    [Fact]
    public void Thunder_wave_from_the_catalog_cannot_paralyze_electric_or_ground_types()
    {
        var vsRaichu = PlayFirstTurn(
            new PokemonSetSpec("Jolteon", ["Thunder Wave"], "Volt Absorb"),
            new PokemonSetSpec("Raichu", ["Thunderbolt"], "Static"));
        var vsGolem = PlayFirstTurn(
            new PokemonSetSpec("Jolteon", ["Thunder Wave"], "Volt Absorb"),
            new PokemonSetSpec("Golem", ["Earthquake"], "Sturdy"));

        Assert.Contains(new Immune(SideId.P2, 0, "Raichu"), vsRaichu);
        Assert.Contains(new Immune(SideId.P2, 0, "Golem"), vsGolem);
    }

    [Fact]
    public void Swords_dance_from_the_catalog_raises_attack()
    {
        var events = PlayFirstTurn(
            new PokemonSetSpec("Scyther", ["Swords Dance"], "Technician"),
            new PokemonSetSpec("Snorlax", ["Body Slam"], "Thick Fat"));

        Assert.Contains(new StatStageChanged(SideId.P1, 0, "Scyther", BoostStat.Attack, 2, 2), events);
    }

    [Fact]
    public void Builder_maps_crit_stage_and_fixed_damage()
    {
        var team = Builder.Build([new PokemonSetSpec("Charizard", ["Slash", "Dragon Rage"], "Blaze")]).Team;

        Assert.Equal(2, team[0].Moves[0].CritRatio);
        Assert.Equal(40, team[0].Moves[1].FixedDamage);
    }
}
