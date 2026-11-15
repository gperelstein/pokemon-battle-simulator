using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;
using static PokemonBattle.Engine.Tests.ItemMoveTests;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Abilities around held items (Frisk, Pickup, Unburden; Sticky Hold is in <see cref="ItemMoveTests"/>),
/// checked against Showdown's data/abilities.ts, which the gen6-gen8 ability mods leave unchanged for
/// them (only ratings differ), and sim/battle.ts (usedItemThisTurn resets at the start of each turn).
/// </summary>
public class ItemAbilityTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static IReadOnlyList<string> Turn(Battle battle, int p1Move = 0, int p2Move = 0)
    {
        battle.Submit(SideId.P1, Move(moveIndex: p1Move));
        return Describe(battle.Submit(SideId.P2, Move(moveIndex: p2Move)).Events);
    }

    private static BattlePokemon Active(Battle battle, SideId side, int slot = 0) => battle.GetSide(side).Active[slot]!;

    private static string FirstMover(IEnumerable<string> events) =>
        events.First(e => e.Contains(" uses ")).Split(' ')[1];

    // ---- Frisk (onStart: every foe's item) ----

    [Fact]
    public void Frisk_reveals_the_items_of_every_foe_on_switch_in()
    {
        var battle = Doubles(
            [WithAbility(Mon("Wigglytuff", 50, moves: [Splash]), "frisk", breakable: false), Mon("Ally", 40, moves: [Splash])],
            [Holding(Mon("Foe", 30, moves: [Splash]), "leftovers"), Mon("Other", 20, moves: [Splash])]);

        var events = Describe(battle.Start());

        Assert.Equal(["P1.0 Wigglytuff frisks leftovers of P2.0"], events.Where(e => e.Contains("frisks")));
    }

    [Fact]
    public void Frisk_says_nothing_when_no_foe_holds_an_item()
    {
        var battle = Singles(
            [WithAbility(Mon("Wigglytuff", 50, moves: [Splash]), "frisk", breakable: false)], [Mon("Foe", 30, moves: [Splash])]);

        Assert.DoesNotContain(Describe(battle.Start()), e => e.Contains("frisk"));
    }

    // ---- Pickup (residual order 28, sub-order 2) ----

    [Fact]
    public void Pickup_picks_up_an_item_another_pokemon_used_this_turn()
    {
        var battle = Singles(
            [WithAbility(Mon("Meowth", 50, moves: [Splash]), "pickup", breakable: false)],
            [Holding(Mon("Foe", 100, moves: [Tackle, Recycle]), "normalgem")]);
        battle.Start();

        var events = Turn(battle);

        Assert.Equal(["P1.0 Meowth pickup activates", "P1.0 Meowth receives normalgem"],
            events.Where(e => e.Contains("pickup") || e.Contains("receives")));
        Assert.Equal("normalgem", Active(battle, SideId.P1).Item);
        // Picked up, so it is no longer the foe's last item.
        Assert.Contains("P2.0 recycle failed", Turn(battle, p2Move: 1));
    }

    [Fact]
    public void Pickup_does_nothing_while_its_holder_has_an_item()
    {
        var battle = Singles(
            [Holding(WithAbility(Mon("Meowth", 50, moves: [Splash]), "pickup", breakable: false), "leftovers")],
            [Holding(Mon("Foe", 100, moves: [Tackle]), "normalgem")]);
        battle.Start();

        Assert.DoesNotContain(Turn(battle), e => e.Contains("pickup"));
        Assert.Equal("leftovers", Active(battle, SideId.P1).Item);
    }

    [Fact]
    public void Pickup_ignores_items_used_in_an_earlier_turn()
    {
        var battle = Singles(
            [Holding(WithAbility(Mon("Meowth", 50, moves: [Splash]), "pickup", breakable: false), "leftovers")],
            [Holding(Mon("Foe", 100, moves: [Tackle, KnockOff]), "normalgem")]);
        battle.Start();
        Turn(battle);

        var events = Turn(battle, p2Move: 1);

        Assert.Null(Active(battle, SideId.P1).Item);
        Assert.DoesNotContain(events, e => e.Contains("pickup"));
    }

    // ---- Unburden (x2 Speed while the holder has no item after losing one) ----

    [Fact]
    public void Unburden_doubles_speed_after_its_holder_uses_up_its_item()
    {
        var battle = Singles(
            [Holding(WithAbility(Mon("Hitmonlee", 60, moves: [Tackle]), "unburden", breakable: false), "normalgem")],
            [Mon("Foe", 100, moves: [Splash])]);
        battle.Start();
        Assert.Equal(60, Active(battle, SideId.P1).Speed);
        Turn(battle);

        Assert.Equal(120, Active(battle, SideId.P1).Speed);
        Assert.Equal("Hitmonlee", FirstMover(Turn(battle)));
    }

    [Fact]
    public void Unburden_doubles_speed_after_its_holder_flings_its_item()
    {
        var battle = Singles(
            [Holding(WithAbility(Mon("Hitmonlee", 60, moves: [Fling]), "unburden", breakable: false),
                new ItemDefinition("ironball", FlingPower: 130))],
            [Mon("Foe", 100, moves: [Splash])]);
        battle.Start();
        Turn(battle);

        Assert.Equal(120, Active(battle, SideId.P1).Speed);
    }

    [Fact]
    public void Unburden_doubles_speed_after_its_item_is_taken_and_stops_when_it_gets_one()
    {
        var battle = Singles(
            [Holding(WithAbility(Mon("Hitmonlee", 60, moves: [Splash, Trick]), "unburden", breakable: false), "leftovers")],
            [Holding(Mon("Foe", 100, moves: [KnockOff, Splash]), "leftovers")]);
        battle.Start();
        Turn(battle);
        Assert.Equal(120, Active(battle, SideId.P1).Speed);

        // Trick brings the foe's Leftovers: Unburden stops.
        Turn(battle, p1Move: 1, p2Move: 1);

        Assert.Equal("leftovers", Active(battle, SideId.P1).Item);
        Assert.Equal(60, Active(battle, SideId.P1).Speed);
    }

    [Fact]
    public void Unburden_ends_when_its_holder_leaves_the_field()
    {
        var battle = Singles(
            [
                Holding(WithAbility(Mon("Hitmonlee", 60, moves: [Tackle]), "unburden", breakable: false), "normalgem"),
                Mon("Bench", 10, moves: [Splash]),
            ],
            [Mon("Foe", 100, moves: [Splash])]);
        battle.Start();
        Turn(battle);
        battle.Submit(SideId.P1, Switch(0, 1));
        battle.Submit(SideId.P2, Move());
        battle.Submit(SideId.P1, Switch(0, 0));
        battle.Submit(SideId.P2, Move());

        Assert.Equal(60, Active(battle, SideId.P1).Speed);
    }
}
