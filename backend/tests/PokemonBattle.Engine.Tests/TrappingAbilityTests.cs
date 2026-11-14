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

        Assert.Equal(trapped ? new[] { 0 } : [], Request(battle, SideId.P1).TrappedSlots);
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

        Assert.Empty(Request(battle, SideId.P1).TrappedSlots);
    }

    [Fact]
    public void Shadow_tag_does_not_trap_another_shadow_tag()
    {
        var battle = Singles([WithAbility(Target(), "shadowtag"), Mon("Bench", 10)], [Trapper("shadowtag")]);
        battle.Start();

        Assert.Empty(Request(battle, SideId.P1).TrappedSlots);
        Assert.Empty(Request(battle, SideId.P2).TrappedSlots);
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

        Assert.Equal([0], Request(battle, SideId.P1).TrappedSlots);
        Assert.True(battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 0), new SwitchChoice(1, 2))).Accepted);
    }
}
