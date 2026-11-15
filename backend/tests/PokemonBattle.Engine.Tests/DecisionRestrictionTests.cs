using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;
using static PokemonBattle.Engine.Tests.Fixtures;

namespace PokemonBattle.Engine.Tests;

/// <summary>
/// Held items that restrict a turn decision, checked against Showdown's data/items.ts (Choice Band,
/// Choice Specs, Choice Scarf, Assault Vest, Shed Shell), data/conditions.ts choicelock with the
/// data/mods/gen6/conditions.ts override (no onBeforeMove in Gen 6), sim/pokemon.ts
/// getMoveRequestData (disabled moves) and sim/side.ts chooseMove ("Can't move: ... is disabled").
/// </summary>
public class DecisionRestrictionTests
{
    private static readonly MoveDefinition Splash = new(
        "splash", "Splash", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Self);

    private static readonly MoveDefinition Growl = new(
        "growl", "Growl", PokemonType.Normal, MoveCategory.Status, 0, Target: MoveTarget.Normal,
        Boosts: [Stage(BoostStat.Attack, -1)]);

    private static readonly MoveDefinition ThunderWave = new(
        "thunderwave", "Thunder Wave", PokemonType.Electric, MoveCategory.Status, 0, Priority: 1,
        Target: MoveTarget.Normal, Status: MajorStatus.Paralysis);

    private static PokemonSet Holding(PokemonSet set, string item) => set with { Item = item };

    private static DecisionRequest Request(Battle battle, SideId side) =>
        battle.PendingRequests.Single(r => r.Side == side && r.Kind == DecisionKind.Turn);

    private static void PlayTurn(Battle battle, Decision p1, Decision p2)
    {
        Assert.True(battle.Submit(SideId.P1, p1).Accepted);
        Assert.True(battle.Submit(SideId.P2, p2).Accepted);
    }

    [Fact]
    public void Turn_requests_without_restricting_items_disable_no_move()
    {
        var battle = Singles([Mon("User", 100, moves: [Tackle, Growl])], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        PlayTurn(battle, Move(moveIndex: 0), Move());

        Assert.Empty(Request(battle, SideId.P1).DisabledMoves);
        Assert.True(battle.Submit(SideId.P1, Move(moveIndex: 1)).Accepted);
    }

    [Theory]
    [InlineData("choiceband")]
    [InlineData("choicespecs")]
    [InlineData("choicescarf")]
    public void A_choice_item_locks_its_holder_into_the_first_move_it_uses(string item)
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Tackle, QuickAttack, Growl]), item)], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        // No lock before the holder has used a move.
        Assert.Empty(Request(battle, SideId.P1).DisabledMoves);
        PlayTurn(battle, Move(moveIndex: 1), Move());

        Assert.Equal([0, 2], Request(battle, SideId.P1).DisabledMoves[0]);
    }

    [Fact]
    public void Choosing_a_move_disabled_by_a_choice_lock_is_rejected()
    {
        var battle = Singles(
            [Holding(Mon("Snorlax", 100, moves: [Tackle, QuickAttack]), "choiceband")], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        PlayTurn(battle, Move(moveIndex: 0), Move());

        var result = battle.Submit(SideId.P1, Move(moveIndex: 1));

        Assert.False(result.Accepted);
        Assert.Equal("Can't move: Snorlax's Quick Attack is disabled.", result.Error);
        Assert.True(battle.Submit(SideId.P1, Move(moveIndex: 0)).Accepted);
    }

    [Fact]
    public void A_choice_locked_pokemon_can_still_switch_and_the_lock_ends_when_it_leaves_the_field()
    {
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Tackle, QuickAttack]), "choicescarf"), Mon("Bench", 10)],
            [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();
        PlayTurn(battle, Move(moveIndex: 0), Move());

        PlayTurn(battle, Switch(0, 1), Move());
        PlayTurn(battle, Switch(0, 0), Move());

        Assert.Empty(Request(battle, SideId.P1).DisabledMoves);
        Assert.True(battle.Submit(SideId.P1, Move(moveIndex: 1)).Accepted);
    }

    [Fact]
    public void A_choice_item_does_not_lock_a_holder_that_could_not_use_its_move()
    {
        // Full paralysis stops the move before ModifyMove, so the item never adds its lock.
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Tackle, QuickAttack]), "choiceband")],
            [Mon("Foe", 50, moves: [ThunderWave, Splash])],
            new PercentRolls().Script(0, 4, 0, 3));
        battle.Start();
        PlayTurn(battle, Move(moveIndex: 0), Move(moveIndex: 0));

        Assert.Empty(Request(battle, SideId.P1).DisabledMoves);
    }

    [Fact]
    public void Choice_locks_are_reported_per_slot_in_doubles()
    {
        var battle = Doubles(
            [Mon("Free", 100, moves: [Tackle, QuickAttack]), Holding(Mon("Locked", 90, moves: [Tackle, QuickAttack]), "choicespecs")],
            [Mon("Foe", 50, moves: [Splash]), Mon("Foe2", 40, moves: [Splash])]);
        battle.Start();
        PlayTurn(battle, new Decision(new MoveChoice(0, 0), new MoveChoice(1, 1)),
            new Decision(new MoveChoice(0, 0), new MoveChoice(1, 0)));

        var request = Request(battle, SideId.P1);

        Assert.Equal([1], request.DisabledMoves.Keys);
        Assert.Equal([0], request.DisabledMoves[1]);
        Assert.False(battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 1), new MoveChoice(1, 0))).Accepted);
        Assert.True(battle.Submit(SideId.P1, new Decision(new MoveChoice(0, 1), new MoveChoice(1, 1))).Accepted);
    }

    [Fact]
    public void Assault_vest_keeps_its_holder_from_choosing_status_moves()
    {
        var battle = Singles(
            [Holding(Mon("Snorlax", 100, moves: [Tackle, Growl, Splash]), "assaultvest")], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Assert.Equal([1, 2], Request(battle, SideId.P1).DisabledMoves[0]);
        var result = battle.Submit(SideId.P1, Move(moveIndex: 1));
        Assert.Equal("Can't move: Snorlax's Growl is disabled.", result.Error);
        Assert.True(battle.Submit(SideId.P1, Move(moveIndex: 0)).Accepted);
    }

    [Fact]
    public void Restrictions_that_would_leave_no_move_are_not_applied_until_struggle_exists()
    {
        // Showdown would make the holder use Struggle, which the engine does not have yet.
        var battle = Singles(
            [Holding(Mon("User", 100, moves: [Growl, Splash]), "assaultvest")], [Mon("Foe", 50, moves: [Splash])]);
        battle.Start();

        Assert.Empty(Request(battle, SideId.P1).DisabledMoves);
        Assert.True(battle.Submit(SideId.P1, Move(moveIndex: 1)).Accepted);
    }

    [Fact]
    public void Shed_shell_lets_a_trapped_holder_switch()
    {
        var trapper = WithAbility(Mon("Trapper", 50, moves: [Splash]), "shadowtag");
        var battle = Singles([Holding(Mon("Holder", 100, moves: [Splash]), "shedshell"), Mon("Bench", 10)], [trapper]);
        battle.Start();

        Assert.Empty(Request(battle, SideId.P1).TrappedSlots);
        Assert.True(battle.Submit(SideId.P1, Switch(0, 1)).Accepted);
    }

    [Fact]
    public void Without_shed_shell_the_same_pokemon_stays_trapped()
    {
        var trapper = WithAbility(Mon("Trapper", 50, moves: [Splash]), "shadowtag");
        var battle = Singles([Holding(Mon("Holder", 100, moves: [Splash]), "leftovers"), Mon("Bench", 10)], [trapper]);
        battle.Start();

        Assert.Equal([0], Request(battle, SideId.P1).TrappedSlots);
    }
}
