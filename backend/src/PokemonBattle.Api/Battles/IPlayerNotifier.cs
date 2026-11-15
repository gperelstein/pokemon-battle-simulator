using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Api.Battles;

/// <summary>Pushes a battle update to the connections of one player.</summary>
public interface IPlayerNotifier
{
    Task NotifyAsync(Guid battleId, SideId side, PlayerUpdate update);
}
