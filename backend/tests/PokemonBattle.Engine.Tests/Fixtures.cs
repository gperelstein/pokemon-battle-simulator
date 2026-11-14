using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Tests;

internal static class Fixtures
{
    public static readonly MoveDefinition Tackle =
        new("tackle", "Tackle", PokemonType.Normal, MoveCategory.Physical, 50);

    public static readonly MoveDefinition QuickAttack =
        new("quickattack", "Quick Attack", PokemonType.Normal, MoveCategory.Physical, 40, Priority: 1);

    public static readonly MoveDefinition VoltSwitch =
        new("voltswitch", "Volt Switch", PokemonType.Electric, MoveCategory.Special, 70, SelfSwitch: true);

    public static readonly MoveDefinition Surf =
        new("surf", "Surf", PokemonType.Water, MoveCategory.Special, 90, Target: MoveTarget.AllAdjacentFoes);

    public static PokemonSet Mon(string species, int speed, int hp = 400, params MoveDefinition[] moves) =>
        new(species, [PokemonType.Normal], 50, new Stats(hp, 100, 100, 100, 100, speed),
            moves.Length == 0 ? [Tackle] : moves);

    public static Battle Singles(PokemonSet[] p1, PokemonSet[] p2) =>
        Battle.Create(BattleFormat.Singles, p1, p2, new MaxRandom());

    public static Battle Doubles(PokemonSet[] p1, PokemonSet[] p2) =>
        Battle.Create(BattleFormat.Doubles, p1, p2, new MaxRandom());

    public static Decision Move(int slot = 0, int moveIndex = 0, int target = 0) =>
        new(new MoveChoice(slot, moveIndex, target));

    public static Decision Switch(int slot, int teamIndex) => new(new SwitchChoice(slot, teamIndex));

    /// <summary>Compact, amount-agnostic description of events, so tests can assert ordering.</summary>
    public static IReadOnlyList<string> Describe(IEnumerable<BattleEvent> events) => events.Select(e => e switch
    {
        TurnStarted t => $"turn {t.Turn}",
        SwitchedIn s => $"{s.Side}.{s.Slot} in {s.Species}",
        SwitchedOut s => $"{s.Side}.{s.Slot} out {s.Species}",
        MoveUsed m => $"{m.Side}.{m.Slot} {m.Species} uses {m.MoveId}",
        MoveFailed m => $"{m.Side}.{m.Slot} {m.MoveId} failed",
        DamageDealt d => $"{d.Side}.{d.Slot} {d.Species} damaged",
        Fainted f => $"{f.Side}.{f.Slot} {f.Species} fainted",
        DecisionRequested r => $"request {r.Request.Side} {r.Request.Kind} [{string.Join(",", r.Request.Slots)}]",
        BattleEnded b => $"winner {b.Winner?.ToString() ?? "none"}",
        _ => e.ToString()!,
    }).ToArray();
}

/// <summary>Always returns the highest value: max damage roll and deterministic tie-breaks.</summary>
internal sealed class MaxRandom : IBattleRandom
{
    public int Next(int minInclusive, int maxExclusive) => maxExclusive - 1;
}
