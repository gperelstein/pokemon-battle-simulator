using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Catalog;

/// <summary>Generation 6 type effectiveness.</summary>
public sealed class TypeChart
{
    private readonly IReadOnlyDictionary<PokemonType, IReadOnlyDictionary<PokemonType, double>> _damageTaken;

    internal TypeChart(IReadOnlyDictionary<PokemonType, IReadOnlyDictionary<PokemonType, double>> damageTaken)
    {
        _damageTaken = damageTaken;
    }

    /// <summary>Combined multiplier of an attacking type against every defending type (0, 0.25 ... 4).</summary>
    public double Effectiveness(PokemonType attacking, IEnumerable<PokemonType> defending) =>
        defending.Aggregate(1.0, (total, type) => total * _damageTaken[type][attacking]);
}
