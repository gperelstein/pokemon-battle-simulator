using Akka.Actor;
using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Actors;

/// <summary>
/// Owns one <see cref="Battle"/> and serializes access to it. The actor holds no rules: it forwards
/// decisions to the engine and pushes the resulting events, plus each player's own pending
/// request, to the players. Mid-turn requests (Volt Switch, U-turn...) therefore reach only the
/// affected player while the opponent receives the events with no request.
/// </summary>
public sealed class BattleActor : ReceiveActor
{
    private readonly Battle _battle;
    private readonly IReadOnlyDictionary<SideId, IActorRef> _players;

    public BattleActor(Battle battle, IReadOnlyDictionary<SideId, IActorRef> players)
    {
        _battle = battle;
        _players = players;

        Receive<StartBattle>(_ => Publish(_battle.Start()));
        Receive<SubmitDecision>(Handle);
    }

    public static Props Props(Battle battle, IReadOnlyDictionary<SideId, IActorRef> players) =>
        Akka.Actor.Props.Create(() => new BattleActor(battle, players));

    private void Handle(SubmitDecision message)
    {
        var before = PendingRequestOf(message.Side);
        var result = _battle.Submit(message.Side, message.Decision);
        if (!result.Accepted)
        {
            var after = PendingRequestOf(message.Side);
            Sender.Tell(new DecisionRejected(message.Side, result.Error!, after));
            if (!ReferenceEquals(before, after) && _players.TryGetValue(message.Side, out var player))
            {
                // The rejection changed the request (a revealed trap): only that player is told.
                player.Tell(new BattleUpdate([], after));
            }

            return;
        }

        Sender.Tell(new DecisionAccepted(message.Side));
        if (result.Events.Count > 0)
        {
            Publish(result.Events);
        }
    }

    private DecisionRequest? PendingRequestOf(SideId side) => _battle.PendingRequests.FirstOrDefault(r => r.Side == side);

    private void Publish(IReadOnlyList<BattleEvent> events)
    {
        foreach (var (side, player) in _players)
        {
            player.Tell(new BattleUpdate(events, PendingRequestOf(side)));
        }

        if (_battle.IsOver)
        {
            Context.Stop(Self);
        }
    }
}
