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

    /// <summary>Speed used for turn order, including its stat stage.</summary>
    public int Speed => StatStages.ApplyToStat(Set.Stats.Speed, GetStage(BoostStat.Speed));

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
