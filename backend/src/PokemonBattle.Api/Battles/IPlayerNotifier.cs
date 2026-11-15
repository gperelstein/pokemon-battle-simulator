using PokemonBattle.Actors;
using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Api.Battles;

/// <summary>Pushes a battle update to the connections of one player.</summary>
public interface IPlayerNotifier
{
    Task NotifyAsync(Guid battleId, SideId side, BattleUpdate update);
}

/// <summary>Used until a transport is registered: updates are only kept in the session.</summary>
internal sealed class NullPlayerNotifier : IPlayerNotifier
{
    public Task NotifyAsync(Guid battleId, SideId side, BattleUpdate update) => Task.CompletedTask;
}
