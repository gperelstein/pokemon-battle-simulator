using PokemonBattle.Engine.Pokemon;

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

    /// <summary>Stages are volatile: they are lost when the Pokémon leaves the field.</summary>
    internal void ClearStages() => Array.Clear(_stages);
}
