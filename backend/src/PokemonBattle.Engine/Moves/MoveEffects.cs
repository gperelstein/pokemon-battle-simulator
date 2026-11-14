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

/// <summary>Fraction of the target's max HP restored by a healing move (Recover: 1/2), rounded half up.</summary>
public sealed record HealFraction(int Numerator, int Denominator);

/// <summary>Move properties that abilities and items react to (Showdown's move flags).</summary>
[Flags]
public enum MoveFlags
{
    None = 0,

    /// <summary>Makes contact (Tough Claws, Static, Rough Skin...).</summary>
    Contact = 1,

    /// <summary>Punching move (Iron Fist).</summary>
    Punch = 2,

    /// <summary>Aura or pulse move (Mega Launcher).</summary>
    Pulse = 4,

    /// <summary>Sound-based move (Soundproof).</summary>
    Sound = 8,

    /// <summary>Ball or bomb move (Bulletproof).</summary>
    Bullet = 16,

    /// <summary>Biting move (Strong Jaw).</summary>
    Bite = 32,
}
