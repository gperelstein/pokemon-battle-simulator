using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Catalog;

/// <summary>Generation 6 type effectiveness, backing the engine's <see cref="ITypeChart"/> port.</summary>
public sealed class TypeChart : ITypeChart
{
    private readonly IReadOnlyDictionary<PokemonType, IReadOnlyDictionary<PokemonType, double>> _damageTaken;
    private readonly IReadOnlyDictionary<PokemonType, IReadOnlySet<TypeCondition>> _conditionImmunities;

    internal TypeChart(
        IReadOnlyDictionary<PokemonType, IReadOnlyDictionary<PokemonType, double>> damageTaken,
        IReadOnlyDictionary<PokemonType, IReadOnlySet<TypeCondition>> conditionImmunities)
    {
        _damageTaken = damageTaken;
        _conditionImmunities = conditionImmunities;
    }

    /// <summary>Combined multiplier of an attacking type against every defending type (0, 0.25 ... 4).</summary>
    public double Effectiveness(PokemonType attacking, IReadOnlyList<PokemonType> defending) =>
        defending.Aggregate(1.0, (total, type) => total * _damageTaken[type][attacking]);

    public bool IsImmune(IReadOnlyList<PokemonType> defending, TypeCondition condition) =>
        defending.Any(type => _conditionImmunities.TryGetValue(type, out var immunities) && immunities.Contains(condition));

    /// <summary>Maps Showdown condition ids (brn, par, psn, tox, frz, powder...) to engine conditions.</summary>
    internal static TypeCondition ToCondition(string id) => id switch
    {
        "brn" => TypeCondition.Burn,
        "par" => TypeCondition.Paralysis,
        "psn" or "tox" => TypeCondition.Poison,
        "frz" => TypeCondition.Freeze,
        "powder" => TypeCondition.Powder,
        "sandstorm" => TypeCondition.Sandstorm,
        "hail" => TypeCondition.Hail,
        "trapped" => TypeCondition.Trapped,
        _ => throw new InvalidOperationException($"Unknown type condition '{id}'."),
    };
}
