using PokemonBattle.Engine.Pokemon;
using PokemonBattle.Engine.Moves;

namespace PokemonBattle.Engine.Battles;

/// <summary>Mutable in-battle state of a team member.</summary>
public sealed class BattlePokemon
{
    private readonly int[] _stages = new int[Enum.GetValues<BoostStat>().Length];

    internal BattlePokemon(SideId side, int teamIndex, PokemonSet set)
    {
        Side = side;
        TeamIndex = teamIndex;
        Set = set;
        CurrentHp = set.Stats.Hp;
    }

    public SideId Side { get; }
    public int TeamIndex { get; }
    public PokemonSet Set { get; }
    public int CurrentHp { get; private set; }
    public bool IsFainted => CurrentHp == 0;

    /// <summary>Major status, if any. It stays when the Pokémon switches out.</summary>
    public MajorStatus? Status { get; private set; }

    /// <summary>Remaining sleep counter; the Pokémon wakes when a move attempt brings it to 0.</summary>
    internal int SleepTurns { get; set; }

    /// <summary>Bad poison counter: damage is n/16 of max HP. Restarts when switching in.</summary>
    internal int ToxicStage { get; set; }

    /// <summary>
    /// Speed used for turn order: its stat stage, then paralysis (Gen 6: x0.25, floored).
    /// </summary>
    public int Speed
    {
        get
        {
            var speed = StatStages.ApplyToStat(Set.Stats.Speed, GetStage(BoostStat.Speed));
            return Status == MajorStatus.Paralysis ? speed * 25 / 100 : speed;
        }
    }

    /// <summary>Weight used by weight-based moves, in hectograms (at least 1).</summary>
    public int WeightHg => Math.Max(1, Set.WeightHg);

    /// <summary>Sum of the positive stat stages (Stored Power, Punishment).</summary>
    public int PositiveStages => _stages.Where(stage => stage > 0).Sum();

    /// <summary>Damage taken from moves this turn, oldest first (Counter, Revenge, Metal Burst...).</summary>
    internal List<HitTaken> HitsTakenThisTurn { get; } = [];

    /// <summary>Took damage of any kind this turn (Assurance).</summary>
    internal bool HurtThisTurn { get; set; }

    /// <summary>Entered the field this turn (Payback is not boosted against it).</summary>
    internal bool NewlySwitched { get; set; }

    /// <summary>Two-turn move being charged (Solar Beam, Fly...), or null.</summary>
    internal string? ChargingMoveId { get; private set; }

    /// <summary>Choice repeated automatically on the turn after charging.</summary>
    internal MoveChoice? ChargedChoice { get; private set; }

    /// <summary>Must spend its next turn recharging (Hyper Beam...).</summary>
    internal bool MustRecharge { get; set; }

    /// <summary>Its next action is fixed, so its player is not asked for a decision.</summary>
    internal bool IsLocked => ChargedChoice is not null || MustRecharge;

    internal void StartCharging(string moveId, MoveChoice choice)
    {
        ChargingMoveId = moveId;
        ChargedChoice = choice;
    }

    internal void StopCharging()
    {
        ChargingMoveId = null;
        ChargedChoice = null;
    }

    /// <summary>Volatile state is lost when the Pokémon leaves the field.</summary>
    internal void LeaveField()
    {
        Array.Clear(_stages);
        StopCharging();
        MustRecharge = false;
    }

    internal void ResetTurnState()
    {
        HitsTakenThisTurn.Clear();
        HurtThisTurn = false;
        NewlySwitched = false;
    }

    internal int Heal(int amount)
    {
        var healed = Math.Min(amount, Set.Stats.Hp - CurrentHp);
        CurrentHp += healed;
        return healed;
    }

    internal void SetStatus(MajorStatus status)
    {
        Status = status;
        ToxicStage = 0;
    }

    internal void CureStatus() => Status = null;

    /// <summary>Current stage of <paramref name="stat"/>, from -6 to +6.</summary>
    public int GetStage(BoostStat stat) => _stages[(int)stat];

    internal int TakeDamage(int amount)
    {
        var dealt = Math.Min(amount, CurrentHp);
        CurrentHp -= dealt;
        return dealt;
    }

    /// <summary>Changes a stage within [-6, 6]. Returns the change actually applied (0 at the limit).</summary>
    internal int ChangeStage(BoostStat stat, int stages)
    {
        var current = _stages[(int)stat];
        var next = Math.Clamp(current + stages, StatStages.Min, StatStages.Max);
        _stages[(int)stat] = next;
        return next - current;
    }
}

/// <param name="Slot">Slot the attacker occupied when it hit (Counter aims back at it).</param>
internal sealed record HitTaken(BattlePokemon Source, SideId Side, int Slot, int Damage, MoveCategory Category);
