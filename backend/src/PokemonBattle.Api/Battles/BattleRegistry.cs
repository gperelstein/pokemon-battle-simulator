using System.Collections.Concurrent;
using Akka.Actor;
using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Api.Battles;

/// <summary>Every hosted battle, by id, in memory.</summary>
public sealed class BattleRegistry(ActorSystem system, ITypeChart typeChart, IPlayerNotifier notifier)
{
    private readonly ConcurrentDictionary<Guid, BattleSession> _sessions = new();

    public BattleSession? Find(Guid battleId) => _sessions.GetValueOrDefault(battleId);

    /// <summary>Opens a battle with the creator's (already validated) team.</summary>
    internal PlayerTicket Create(BattleFormat format, IReadOnlyList<PokemonSet> team)
    {
        var session = new BattleSession(Guid.NewGuid(), format, team, system, typeChart, notifier);
        _sessions[session.Id] = session;
        return session.CreatorTicket;
    }
}
