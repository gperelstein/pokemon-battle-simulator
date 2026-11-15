using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>Observable facts produced while the battle runs, in the order they happened.</summary>
public abstract record BattleEvent;

public sealed record TurnStarted(int Turn) : BattleEvent;

public sealed record SwitchedIn(SideId Side, int Slot, int TeamIndex, string Species) : BattleEvent;

public sealed record SwitchedOut(SideId Side, int Slot, int TeamIndex, string Species) : BattleEvent;

public sealed record MoveUsed(SideId Side, int Slot, string Species, string MoveId) : BattleEvent;

public sealed record MoveFailed(SideId Side, int Slot, string MoveId, string Reason) : BattleEvent;

/// <summary>The move was super effective (&gt; 1) or not very effective (&lt; 1) against the target.</summary>
public sealed record Effectiveness(SideId Side, int Slot, double Multiplier) : BattleEvent;

public sealed record CriticalHit(SideId Side, int Slot) : BattleEvent;

/// <summary>The target's types make it immune to the move.</summary>
public sealed record Immune(SideId Side, int Slot, string Species) : BattleEvent;

public sealed record DamageDealt(SideId Side, int Slot, string Species, int Amount, int RemainingHp) : BattleEvent;

public sealed record Fainted(SideId Side, int Slot, string Species) : BattleEvent;

public sealed record DecisionRequested(DecisionRequest Request) : BattleEvent;

/// <param name="Winner">Null when both sides lost at the same time (draw).</param>
public sealed record BattleEnded(SideId? Winner) : BattleEvent;

/// <summary>The move missed the Pokémon in this slot (accuracy check failed).</summary>
public sealed record Missed(SideId Side, int Slot, string Species) : BattleEvent;

/// <param name="Delta">Stages actually gained (positive) or lost (negative).</param>
/// <param name="Stage">Resulting stage, from -6 to +6.</param>
public sealed record StatStageChanged(SideId Side, int Slot, string Species, BoostStat Stat, int Delta, int Stage)
    : BattleEvent;

/// <summary>The stat could not go any higher (<paramref name="Raise"/>) or lower: it is already at +6 or -6.</summary>
public sealed record StatStageAtLimit(SideId Side, int Slot, string Species, BoostStat Stat, bool Raise) : BattleEvent;

public sealed record StatusInflicted(SideId Side, int Slot, string Species, MajorStatus Status) : BattleEvent;

/// <summary>The status ended: the Pokémon woke up or thawed out.</summary>
public sealed record StatusCured(SideId Side, int Slot, string Species, MajorStatus Status) : BattleEvent;

/// <summary>The Pokémon lost its action because of <paramref name="Reason"/> (asleep, frozen, fully paralyzed).</summary>
public sealed record CantMove(SideId Side, int Slot, string Species, MajorStatus Reason) : BattleEvent;

/// <summary>End-of-turn damage from burn or poison.</summary>
public sealed record StatusDamage(SideId Side, int Slot, string Species, MajorStatus Status, int Amount, int RemainingHp)
    : BattleEvent;

/// <summary>A one-hit KO move landed on the Pokémon in this slot.</summary>
public sealed record OneHitKo(SideId Side, int Slot) : BattleEvent;

public sealed record Healed(SideId Side, int Slot, string Species, int Amount, int RemainingHp) : BattleEvent;

/// <summary>Magnitude rolled its strength (4 to 10), which sets its power.</summary>
public sealed record MagnitudeActivated(SideId Side, int Slot, int Magnitude) : BattleEvent;

public sealed record WeatherStarted(Weather Weather) : BattleEvent;

/// <summary>End-of-turn reminder that the weather is still active.</summary>
public sealed record WeatherContinues(Weather Weather) : BattleEvent;

public sealed record WeatherEnded(Weather Weather) : BattleEvent;

/// <summary>End-of-turn damage from sandstorm or hail.</summary>
public sealed record WeatherDamage(SideId Side, int Slot, string Species, Weather Weather, int Amount, int RemainingHp)
    : BattleEvent;

/// <summary>First turn of a two-turn move: the user prepares and will attack next turn.</summary>
public sealed record MoveCharging(SideId Side, int Slot, string Species, string MoveId) : BattleEvent;

/// <summary>The Pokémon spends its turn recharging after Hyper Beam or a similar move.</summary>
public sealed record Recharging(SideId Side, int Slot, string Species) : BattleEvent;

/// <summary>The Pokémon's ability made it immune to the move or effect (Levitate, Volt Absorb, Limber...).</summary>
public sealed record AbilityImmune(SideId Side, int Slot, string Species, string Ability) : BattleEvent;

/// <summary>The Pokémon's ability took effect (Volt Absorb healing, Flash Fire, Sturdy, Damp...).</summary>
public sealed record AbilityActivated(SideId Side, int Slot, string Species, string Ability) : BattleEvent;

/// <summary>The Pokémon's ability stopped another Pokémon from lowering its stats (Clear Body...).</summary>
public sealed record StatDropPrevented(SideId Side, int Slot, string Species, string Ability) : BattleEvent;

/// <summary>Trace copied the ability of the foe in (<paramref name="FoeSide"/>, <paramref name="FoeSlot"/>).</summary>
public sealed record AbilityTraced(SideId Side, int Slot, string Species, string Ability, SideId FoeSide, int FoeSlot)
    : BattleEvent;

/// <summary>Forewarn revealed the foe's most powerful move.</summary>
public sealed record ForewarnAlert(SideId Side, int Slot, string Species, string MoveId, SideId FoeSide, int FoeSlot)
    : BattleEvent;

/// <summary>Damage dealt by an ability (Aftermath) to the Pokémon in this slot.</summary>
public sealed record AbilityDamage(SideId Side, int Slot, string Species, string Ability, int Amount, int RemainingHp)
    : BattleEvent;

/// <summary>
/// The held item took effect without being used up (Quick Claw letting its holder move first, Focus
/// Band, Safety Goggles, an Air Balloon announced on switch-in), or is the source of the heal or
/// status that follows (Leftovers, Shell Bell, Toxic Orb: Showdown's "[from] item: ...").
/// </summary>
public sealed record ItemActivated(SideId Side, int Slot, string Species, string Item) : BattleEvent;

/// <summary>
/// The held item was used up or lost (a Gem, Focus Sash, Weakness Policy, a popped Air Balloon...):
/// Showdown's "-enditem".
/// </summary>
public sealed record ItemConsumed(SideId Side, int Slot, string Species, string Item) : BattleEvent;

/// <summary>
/// Damage dealt by a held item to the Pokémon in this slot: its own (Life Orb, Black Sludge, Sticky
/// Barb) or the Rocky Helmet of the Pokémon it made contact with.
/// </summary>
public sealed record ItemDamage(SideId Side, int Slot, string Species, string Item, int Amount, int RemainingHp)
    : BattleEvent;

/// <summary>
/// The Pokémon in this slot is forced out (Roar, Dragon Tail, Red Card) and replaced by a random
/// healthy ally without asking its player (Showdown's "drag"). The usual <see cref="SwitchedOut"/>
/// and <see cref="SwitchedIn"/> follow.
/// </summary>
public sealed record ForcedOut(SideId Side, int Slot, string Species) : BattleEvent;

/// <summary>White Herb reset the Pokémon's lowered stat stages to 0 (Showdown's "-clearnegativeboost").</summary>
public sealed record NegativeStagesCleared(SideId Side, int Slot, string Species) : BattleEvent;

/// <summary>
/// A volatile condition started on the Pokémon (Showdown's "-start"), such as the Focus Energy effect
/// a Lansat Berry gives.
/// </summary>
public sealed record ConditionStarted(SideId Side, int Slot, string Species, string Condition) : BattleEvent;

/// <summary>The Pokémon got an item back (Showdown's "-item"), such as the berry Harvest restores.</summary>
public sealed record ItemReceived(SideId Side, int Slot, string Species, string Item) : BattleEvent;
