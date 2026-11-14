using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Catalog;

/// <summary>Generation 6 type effectiveness, backing the engine's <see cref="ITypeChart"/> port.</summary>
public sealed class TypeChart : ITypeChart
{
    private readonly IReadOnlyDictionary<PokemonType, IReadOnlyDictionary<PokemonType, double>> _damageTaken;

    internal TypeChart(IReadOnlyDictionary<PokemonType, IReadOnlyDictionary<PokemonType, double>> damageTaken)
    {
        _damageTaken = damageTaken;
    }

    /// <summary>Combined multiplier of an attacking type against every defending type (0, 0.25 ... 4).</summary>
    public double Effectiveness(PokemonType attacking, IReadOnlyList<PokemonType> defending) =>
        defending.Aggregate(1.0, (total, type) => total * _damageTaken[type][attacking]);
}
