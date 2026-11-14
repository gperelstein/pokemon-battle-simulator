namespace PokemonBattle.Engine.Pokemon;

/// <summary>
/// Port for type effectiveness. The engine owns the rule; the data comes from the catalog.
/// </summary>
public interface ITypeChart
{
    /// <summary>Combined multiplier of an attacking type against every defending type (0, 0.25 ... 4).</summary>
    double Effectiveness(PokemonType attacking, IReadOnlyList<PokemonType> defending);

    /// <summary>Whether any defending type is naturally immune to the condition (Electric to paralysis...).</summary>
    bool IsImmune(IReadOnlyList<PokemonType> defending, TypeCondition condition);
}
