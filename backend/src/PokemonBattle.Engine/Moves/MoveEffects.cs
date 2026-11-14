using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Moves;

/// <summary>Raises (positive) or lowers (negative) a stat by a number of stages.</summary>
public sealed record StatChange(BoostStat Stat, int Stages);

/// <summary>
/// Chance-based effect applied after a damaging move hits a target (Psychic's Special Defense drop,
/// Flame Charge's Speed boost...).
/// </summary>
/// <param name="Chance">Percentage chance of the effect, 100 meaning always.</param>
/// <param name="TargetBoosts">Stage changes applied to the target that was hit.</param>
/// <param name="SelfBoosts">Stage changes applied to the user.</param>
/// <param name="Status">Major status inflicted on the target (Thunderbolt's paralysis...).</param>
public sealed record SecondaryEffect(
    int Chance,
    IReadOnlyList<StatChange>? TargetBoosts = null,
    IReadOnlyList<StatChange>? SelfBoosts = null,
    MajorStatus? Status = null)
{
    public IReadOnlyList<StatChange> TargetBoosts { get; init; } = TargetBoosts ?? [];
    public IReadOnlyList<StatChange> SelfBoosts { get; init; } = SelfBoosts ?? [];
}
