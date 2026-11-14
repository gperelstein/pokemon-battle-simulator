using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>One player's team and the Pokémon currently occupying its active slots.</summary>
public sealed class Side
{
    private readonly BattlePokemon?[] _active;

    internal Side(SideId id, IReadOnlyList<PokemonSet> team, int activeSlots, BattleField field)
    {
        Id = id;
        Team = team.Select((set, index) => new BattlePokemon(id, index, set, field)).ToArray();
        _active = new BattlePokemon?[activeSlots];
    }

    public SideId Id { get; }
    public IReadOnlyList<BattlePokemon> Team { get; }
    public IReadOnlyList<BattlePokemon?> Active => _active;

    public bool HasLost => Team.All(p => p.IsFainted);

    /// <summary>Slots holding a Pokémon that can still act.</summary>
    public IEnumerable<int> LiveSlots =>
        Enumerable.Range(0, _active.Length).Where(slot => _active[slot] is { IsFainted: false });

    /// <summary>Team members that could be sent in: not fainted and not already active.</summary>
    public IReadOnlyList<BattlePokemon> Bench =>
        Team.Where(p => !p.IsFainted && !_active.Contains(p)).ToArray();

    internal BattlePokemon? Place(int slot, BattlePokemon pokemon)
    {
        var previous = _active[slot];
        _active[slot] = pokemon;
        return previous;
    }
}
