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
        Battle.Create(BattleFormat.Singles, p1, p2, TestTypeChart.Gen6Sample, new MaxRandom());

    public static Battle Doubles(PokemonSet[] p1, PokemonSet[] p2) =>
        Battle.Create(BattleFormat.Doubles, p1, p2, TestTypeChart.Gen6Sample, new MaxRandom());

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
        Effectiveness eff => $"{eff.Side}.{eff.Slot} effectiveness x{eff.Multiplier}",
        CriticalHit c => $"{c.Side}.{c.Slot} critical hit",
        Immune i => $"{i.Side}.{i.Slot} {i.Species} immune",
        Fainted f => $"{f.Side}.{f.Slot} {f.Species} fainted",
        DecisionRequested r => $"request {r.Request.Side} {r.Request.Kind} [{string.Join(",", r.Request.Slots)}]",
        BattleEnded b => $"winner {b.Winner?.ToString() ?? "none"}",
        _ => e.ToString()!,
    }).ToArray();
}

/// <summary>Always returns the highest value: max damage roll, no critical hits, deterministic tie-breaks.</summary>
internal sealed class MaxRandom : IBattleRandom
{
    public int Next(int minInclusive, int maxExclusive) => maxExclusive - 1;
}

/// <summary>Returns the given values in order and records every requested range.</summary>
internal sealed class ScriptedRandom(params int[] values) : IBattleRandom
{
    private int _next;

    public List<(int Min, int Max)> Calls { get; } = [];

    public int Next(int minInclusive, int maxExclusive)
    {
        Calls.Add((minInclusive, maxExclusive));
        var value = values[_next++];
        Assert.InRange(value, minInclusive, maxExclusive - 1);
        return value;
    }
}

/// <summary>A small subset of the Gen 6 chart; every pair not listed is neutral.</summary>
internal sealed class TestTypeChart(Dictionary<(PokemonType Attacking, PokemonType Defending), double> pairs) : ITypeChart
{
    public static readonly TestTypeChart Gen6Sample = new(new()
    {
        [(PokemonType.Water, PokemonType.Fire)] = 2,
        [(PokemonType.Fire, PokemonType.Water)] = 0.5,
        [(PokemonType.Fire, PokemonType.Dragon)] = 0.5,
        [(PokemonType.Electric, PokemonType.Ground)] = 0,
        [(PokemonType.Electric, PokemonType.Water)] = 2,
        [(PokemonType.Fighting, PokemonType.Ghost)] = 0,
        [(PokemonType.Fighting, PokemonType.Rock)] = 2,
        [(PokemonType.Normal, PokemonType.Ghost)] = 0,
    });

    public double Effectiveness(PokemonType attacking, IReadOnlyList<PokemonType> defending) =>
        defending.Aggregate(1.0, (total, type) => total * pairs.GetValueOrDefault((attacking, type), 1));
}
