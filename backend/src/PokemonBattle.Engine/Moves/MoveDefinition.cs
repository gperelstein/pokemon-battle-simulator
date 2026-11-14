using PokemonBattle.Engine.Pokemon;
using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Engine.Moves;

public enum MoveCategory
{
    Physical,
    Special,
    Status,
}

/// <summary>Gen 6 move target kinds (mirrors Pokémon Showdown's targets).</summary>
public enum MoveTarget
{
    /// <summary>One adjacent opposing Pokémon chosen by the player.</summary>
    AdjacentFoe,

    /// <summary>Every adjacent opposing Pokémon (spread move).</summary>
    AllAdjacentFoes,

    /// <summary>
    /// One adjacent Pokémon chosen by the player. Choosing an ally is not supported yet, so it
    /// currently behaves like <see cref="AdjacentFoe"/>.
    /// </summary>
    Normal,

    /// <summary>Any Pokémon chosen by the player (relevant from Triples). Behaves like <see cref="Normal"/>.</summary>
    Any,

    /// <summary>Every adjacent Pokémon, allies included (Earthquake, Surf).</summary>
    AllAdjacent,

    /// <summary>A random adjacent foe (Outrage, Thrash...).</summary>
    RandomNormal,

    /// <summary>Target decided by the move itself (Counter, Mirror Coat...). Treated as <see cref="AdjacentFoe"/>.</summary>
    Scripted,

    Self,
    AdjacentAlly,
    AdjacentAllyOrSelf,
    Allies,
    AllySide,
    AllyTeam,
    FoeSide,
    All,
}

/// <summary>
/// Static move data. <see cref="SelfSwitch"/> marks moves such as U-turn or Volt Switch that
/// force the user's player to pick a replacement in the middle of the turn.
/// </summary>
/// <param name="CritRatio">Critical hit stage: 1 = 1/16, 2 = 1/8, 3 = 1/2, 4 or more = always.</param>
/// <param name="WillCrit">Always lands a critical hit (Storm Throw, Frost Breath).</param>
/// <param name="FixedDamage">Deals exactly this damage (Dragon Rage, Sonic Boom).</param>
/// <param name="LevelDamage">Deals damage equal to the user's level (Seismic Toss, Night Shade).</param>
/// <param name="Accuracy">Percentage chance to hit; null for moves that never miss (Swift, Swords Dance).</param>
/// <param name="Boosts">Stage changes applied to each target (Growl, Sand Attack; the user for self moves).</param>
/// <param name="SelfBoosts">Stage changes the user receives after hitting (Close Combat, Draco Meteor).</param>
/// <param name="SelfBoostChance">Chance of <paramref name="SelfBoosts"/>; null means always (Diamond Storm: 50).</param>
/// <param name="Secondaries">Chance-based effects applied to each target hit.</param>
/// <param name="IgnoreEvasion">Ignores the target's evasion stage (Chip Away, Sacred Sword).</param>
/// <param name="IgnoreDefensive">Ignores the target's defensive stat stage (Chip Away, Sacred Sword).</param>
/// <param name="Status">Major status inflicted on each target (Thunder Wave, Spore, Toxic...).</param>
/// <param name="IgnoreImmunity">
/// Whether type immunities are ignored; null uses the default (status moves ignore them, damaging
/// moves do not). Thunder Wave sets it to false so Ground types stay immune.
/// </param>
/// <param name="Powder">Powder or spore move: Grass types are immune (Gen 6).</param>
/// <param name="Defrost">Thaws its frozen user before moving (Flame Wheel, Scald...).</param>
/// <param name="ThawsTarget">Thaws a frozen target it hits, whatever its type (Scald, Steam Eruption).</param>
/// <param name="SleepUsable">Can be used while asleep (Snore, Sleep Talk).</param>
/// <param name="Ohko">One-hit KO move (Fissure, Guillotine, Horn Drill, Sheer Cold).</param>
/// <param name="SetsWeather">Weather started by the move (Sunny Day, Rain Dance, Sandstorm, Hail).</param>
/// <param name="Heal">Fraction of max HP restored to each target (Recover, Soft-Boiled...).</param>
public sealed record MoveDefinition(
    string Id,
    string Name,
    PokemonType Type,
    MoveCategory Category,
    int BasePower,
    int Priority = 0,
    MoveTarget Target = MoveTarget.AdjacentFoe,
    bool SelfSwitch = false,
    int CritRatio = 1,
    bool WillCrit = false,
    int? FixedDamage = null,
    bool LevelDamage = false,
    int? Accuracy = null,
    IReadOnlyList<StatChange>? Boosts = null,
    IReadOnlyList<StatChange>? SelfBoosts = null,
    int? SelfBoostChance = null,
    IReadOnlyList<SecondaryEffect>? Secondaries = null,
    bool IgnoreEvasion = false,
    bool IgnoreDefensive = false,
    MajorStatus? Status = null,
    bool? IgnoreImmunity = null,
    bool Powder = false,
    bool Defrost = false,
    bool ThawsTarget = false,
    bool SleepUsable = false,
    bool Ohko = false,
    Weather? SetsWeather = null,
    HealFraction? Heal = null)
{
    /// <summary>Whether a target the move's type cannot affect is still hit.</summary>
    public bool IgnoresTypeImmunity => IgnoreImmunity ?? Category == MoveCategory.Status;

    public IReadOnlyList<StatChange> Boosts { get; init; } = Boosts ?? [];
    public IReadOnlyList<StatChange> SelfBoosts { get; init; } = SelfBoosts ?? [];
    public IReadOnlyList<SecondaryEffect> Secondaries { get; init; } = Secondaries ?? [];

    /// <summary>
    /// Damaging move without a fixed base power: its power or damage comes from move-specific logic
    /// (Low Kick, Return, OHKO moves, Counter...). The battle supports a subset of them.
    /// </summary>
    public bool HasVariablePower =>
        Category != MoveCategory.Status && BasePower == 0 && FixedDamage is null && !LevelDamage;
}
