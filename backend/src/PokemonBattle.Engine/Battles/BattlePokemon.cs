using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>Mutable in-battle state of a team member.</summary>
public sealed class BattlePokemon
{
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

    internal int TakeDamage(int amount)
    {
        var dealt = Math.Min(amount, CurrentHp);
        CurrentHp -= dealt;
        return dealt;
    }
}
