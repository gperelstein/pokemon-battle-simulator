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
