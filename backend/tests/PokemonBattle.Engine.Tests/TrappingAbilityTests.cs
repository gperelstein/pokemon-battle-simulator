using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Arena Trap, Magnet Pull and Shadow Tag, checked against Showdown's data/abilities.ts (Gen 6),
/// sim/pokemon.ts tryTrap/isGrounded and sim/side.ts (a trapped Pokémon cannot choose to switch).
/// </summary>
public class TrappingAbilityTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static PokemonSet Trapper(string ability) => WithAbility(Mon("Trapper", 50, moves: [Splash]), ability);

    private static PokemonSet Target(params PokemonType[] types) =>
        Mon("Target", 100, moves: [Splash, VoltSwitch]) with { Types = types.Length == 0 ? [PokemonType.Normal] : types };

    private static DecisionRequest Request(Battle battle, SideId side) => battle.PendingRequests.Single(r => r.Side == side);

    [Theory]
    [InlineData("arenatrap", new[] { PokemonType.Normal }, true)]
    [InlineData("arenatrap", new[] { PokemonType.Flying }, false)] // not grounded
    [InlineData("magnetpull", new[] { PokemonType.Steel }, true)]
    [InlineData("magnetpull", new[] { PokemonType.Normal }, false)]
    [InlineData("shadowtag", new[] { PokemonType.Normal }, true)]
    [InlineData("arenatrap", new[] { PokemonType.Ghost }, false)] // Ghost types cannot be trapped (Gen 6)
    [InlineData("shadowtag", new[] { PokemonType.Ghost }, false)]
    public void Trapping_abilities_keep_some_foes_from_switching(string ability, PokemonType[] types, bool trapped)
    {
        var battle = Singles([Target(types), Mon("Bench", 10)], [Trapper(ability)]);
        battle.Start();

        Assert.Equal(trapped ? new[] { 0 } : [], Request(battle, SideId.P1).MaybeTrappedSlots);
        Assert.Equal(!trapped, battle.Submit(SideId.P1, Switch(0, 1)).Accepted);
    }

    [Fact]
    public void A_trapped_pokemon_is_told_why_it_cannot_switch()
    {
        var battle = Singles([Target(), Mon("Bench", 10)], [Trapper("arenatrap")]);
        battle.Start();

        var result = battle.Submit(SideId.P1, Switch(0, 1));

        Assert.Equal("Can't switch: the active Pokémon is trapped.", result.Error);
        Assert.True(battle.Submit(SideId.P1, Move()).Accepted);
    }

    [Fact]
    public void Levitate_escapes_arena_trap()
    {
        var battle = Singles([WithAbility(Target(), "levitate"), Mon("Bench", 10)], [Trapper("arenatrap")]);
        battle.Start();

        Assert.Empty(Request(battle, SideId.P1).MaybeTrappedSlots);
    }

    [Fact]
    public void Shadow_tag_does_not_trap_another_shadow_tag()
    {
        var battle = Singles([WithAbility(Target(), "shadowtag"), Mon("Bench", 10)], [Trapper("shadowtag")]);
        battle.Start();

        Assert.Empty(Request(battle, SideId.P1).MaybeTrappedSlots);
        Assert.Empty(Request(battle, SideId.P2).MaybeTrappedSlots);
    }

    [Fact]
    public void A_trapped_pokemon_can_still_leave_with_a_self_switch_move()
    {
        var battle = Singles([Target(), Mon("Bench", 10)], [Trapper("shadowtag")]);
        battle.Start();
        battle.Submit(SideId.P1, Move(moveIndex: 1));
        battle.Submit(SideId.P2, Move());

        Assert.Contains(battle.PendingRequests, r => r is { Side: SideId.P1, Kind: DecisionKind.MidTurnSwitch });
        Assert.True(battle.Submit(SideId.P1, Switch(0, 1)).Accepted);
    }

    [Fact]
    public void Trapping_is_decided_per_slot_in_doubles()
    {
        var battle = Doubles(
            [Target(PokemonType.Steel), Target(PokemonType.Normal), Mon("Bench", 10)],
            [Trapper("magnetpull"), Mon("Other", 40, moves: [Splash])]);
        battle.Start();

        Assert.Equal([0], Request(battle, SideId.P1).MaybeTrappedSlots);
        Assert.True(battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new SwitchChoice(1, 2))).Accepted);
    }

    private static readonly string[] DugtrioAbilities = ["sandveil", "arenatrap", "sandforce"];

    private static PokemonSet CouldTrap(string ability) => Trapper(ability) with { SpeciesAbilities = DugtrioAbilities };

    [Fact]
    public void A_trapping_ability_is_hidden_until_a_switch_fails()
    {
        var battle = Singles([Target(), Mon("Bench", 10)], [Trapper("arenatrap")]);
        battle.Start();
        var before = Request(battle, SideId.P1);

        Assert.Empty(before.TrappedSlots);
        Assert.False(battle.Submit(SideId.P1, Switch(0, 1)).Accepted);

        var after = Request(battle, SideId.P1);
        Assert.Equal([0], after.TrappedSlots);
        Assert.Empty(after.MaybeTrappedSlots);
        Assert.Equal((before.Side, before.Kind, before.Slots), (after.Side, after.Kind, after.Slots));
        Assert.Single(battle.PendingRequests, r => r.Side == SideId.P1);
    }

    [Fact]
    public void A_revealed_trap_still_rejects_the_switch_and_lets_the_player_move()
    {
        var battle = Singles([Target(), Mon("Bench", 10)], [Trapper("arenatrap")]);
        battle.Start();
        battle.Submit(SideId.P1, Switch(0, 1));

        var again = battle.Submit(SideId.P1, Switch(0, 1));

        Assert.Equal("Can't switch: the active Pokémon is trapped.", again.Error);
        Assert.Equal([0], Request(battle, SideId.P1).TrappedSlots);
        Assert.True(battle.Submit(SideId.P1, Move()).Accepted);
    }

    [Fact]
    public void The_trap_is_hidden_again_in_the_next_turn_request()
    {
        var battle = Singles([Target(), Mon("Bench", 10)], [Trapper("arenatrap")]);
        battle.Start();
        battle.Submit(SideId.P1, Switch(0, 1));
        battle.Submit(SideId.P1, Move());
        battle.Submit(SideId.P2, Move());

        var next = Request(battle, SideId.P1);
        Assert.Empty(next.TrappedSlots);
        Assert.Equal([0], next.MaybeTrappedSlots);
    }

    [Fact]
    public void A_foe_whose_species_could_trap_marks_the_slot_without_trapping_it()
    {
        var battle = Singles([Target(), Mon("Bench", 10)], [CouldTrap("sandveil")]);
        battle.Start();

        Assert.Equal([0], Request(battle, SideId.P1).MaybeTrappedSlots);
        Assert.Empty(Request(battle, SideId.P1).TrappedSlots);
        Assert.True(battle.Submit(SideId.P1, Switch(0, 1)).Accepted);
    }

    [Theory]
    [InlineData(new[] { PokemonType.Flying }, "")] // not grounded
    [InlineData(new[] { PokemonType.Ghost }, "")] // Ghost types cannot be trapped
    [InlineData(new[] { PokemonType.Normal }, "levitate")]
    public void A_possible_trapping_ability_follows_its_own_conditions(PokemonType[] types, string ability)
    {
        var target = ability.Length == 0 ? Target(types) : WithAbility(Target(types), ability);
        var battle = Singles([target, Mon("Bench", 10)], [CouldTrap("sandveil")]);
        battle.Start();

        Assert.Empty(Request(battle, SideId.P1).MaybeTrappedSlots);
    }

    [Fact]
    public void A_foe_whose_species_cannot_trap_does_not_mark_the_slot()
    {
        var foe = Trapper("sturdy") with { SpeciesAbilities = ["sturdy", "rockhead"] };
        var battle = Singles([Target(), Mon("Bench", 10)], [foe]);
        battle.Start();

        Assert.Empty(Request(battle, SideId.P1).MaybeTrappedSlots);
    }

    [Fact]
    public void A_shed_shell_holder_is_never_marked()
    {
        // Deliberate divergence: Showdown still marks it for a foe's other possible abilities, so the
        // holder would learn the foe's real ability from a missing mark.
        var holder = Target() with { Item = "shedshell" };
        var real = Singles([holder, Mon("Bench", 10)], [Trapper("arenatrap")]);
        var possible = Singles([holder, Mon("Bench", 10)], [CouldTrap("sandveil")]);
        real.Start();
        possible.Start();

        Assert.Empty(Request(real, SideId.P1).MaybeTrappedSlots);
        Assert.Empty(Request(possible, SideId.P1).MaybeTrappedSlots);
    }

    [Fact]
    public void Only_the_failed_switch_reveals_its_slot_in_doubles()
    {
        var magneton = Trapper("magnetpull") with { SpeciesAbilities = ["magnetpull", "sturdy", "analytic"] };
        var battle = Doubles(
            [Target(PokemonType.Steel), Target(PokemonType.Steel), Mon("Bench", 10), Mon("Bench2", 10)],
            [magneton, Mon("Other", 40, moves: [Splash])]);
        battle.Start();

        Assert.False(battle.Submit(SideId.P1, new Decision(new SwitchChoice(0, 2), new SwitchChoice(1, 3))).Accepted);

        Assert.Equal([0], Request(battle, SideId.P1).TrappedSlots);
        Assert.Equal([1], Request(battle, SideId.P1).MaybeTrappedSlots);
    }
}
