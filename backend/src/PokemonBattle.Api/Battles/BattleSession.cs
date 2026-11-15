using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Akka.Actor;
using PokemonBattle.Actors;
using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Api.Battles;

public enum BattleStatus
{
    /// <summary>Created by the first player; waiting for the second one.</summary>
    Waiting,
    Active,
    Over,
}

/// <summary>
/// One hosted battle: the two anonymous players' tokens, the <see cref="BattleActor"/> that owns the
/// engine, and what each player has seen so far (events and pending request), so a player can
/// (re)connect at any time. Sessions live in memory only.
/// </summary>
public sealed class BattleSession
{
    private static readonly TimeSpan SubmitTimeout = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly IReadOnlyList<PokemonSet> _p1Team;
    private readonly ActorSystem _system;
    private readonly ITypeChart _typeChart;
    private readonly IPlayerNotifier _notifier;
    private readonly Dictionary<SideId, string> _tokens = [];
    private readonly Dictionary<SideId, List<BattleEvent>> _events = new() { [SideId.P1] = [], [SideId.P2] = [] };
    private readonly Dictionary<SideId, DecisionRequest?> _requests = new() { [SideId.P1] = null, [SideId.P2] = null };
    private IActorRef? _battle;

    internal BattleSession(Guid id, BattleFormat format, IReadOnlyList<PokemonSet> p1Team, ActorSystem system,
        ITypeChart typeChart, IPlayerNotifier notifier)
    {
        Id = id;
        Format = format;
        _p1Team = p1Team;
        _system = system;
        _typeChart = typeChart;
        _notifier = notifier;
        _tokens[SideId.P1] = NewToken();
    }

    public Guid Id { get; }

    public BattleFormat Format { get; }

    public BattleStatus Status { get; private set; } = BattleStatus.Waiting;

    internal PlayerTicket CreatorTicket => new(Id, SideId.P1, _tokens[SideId.P1]);

    /// <summary>The side a token belongs to, or null (compared in constant time).</summary>
    public SideId? Authenticate(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var given = Encoding.UTF8.GetBytes(token);
        lock (_gate)
        {
            foreach (var (side, expected) in _tokens)
            {
                if (CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(expected)))
                {
                    return side;
                }
            }
        }

        return null;
    }

    public PlayerView ViewOf(SideId side)
    {
        lock (_gate)
        {
            return new PlayerView(_events[side].ToArray(), _requests[side]);
        }
    }

    /// <summary>
    /// Takes the second seat and starts the battle; null when both seats are taken. The engine gets a
    /// fresh random seed per battle.
    /// </summary>
    internal PlayerTicket? Join(IReadOnlyList<PokemonSet> p2Team)
    {
        lock (_gate)
        {
            if (_tokens.ContainsKey(SideId.P2))
            {
                return null;
            }

            _tokens[SideId.P2] = NewToken();
            Status = BattleStatus.Active;
            var battle = Battle.Create(Format, _p1Team, p2Team, _typeChart,
                new SeededBattleRandom(RandomNumberGenerator.GetInt32(int.MaxValue)));
            var players = new Dictionary<SideId, IActorRef>
            {
                [SideId.P1] = _system.ActorOf(PlayerRelay.Props(update => OnUpdate(SideId.P1, update))),
                [SideId.P2] = _system.ActorOf(PlayerRelay.Props(update => OnUpdate(SideId.P2, update))),
            };
            _battle = _system.ActorOf(BattleActor.Props(battle, players), $"battle-{Id}");
            _battle.Tell(new StartBattle());
            return new PlayerTicket(Id, SideId.P2, _tokens[SideId.P2]);
        }
    }

    /// <summary>
    /// Sends a decision for <paramref name="side"/>, which must come from the caller's authenticated
    /// token, never from the client's payload (L5).
    /// </summary>
    public async Task<SubmitOutcome> SubmitAsync(SideId side, Decision decision)
    {
        IActorRef battle;
        lock (_gate)
        {
            if (Status == BattleStatus.Over)
            {
                return new SubmitOutcome(false, "The battle is over.", null);
            }

            if (_battle is null)
            {
                return new SubmitOutcome(false, "The battle has not started yet.", null);
            }

            battle = _battle;
        }

        object reply;
        try
        {
            reply = await battle.Ask<object>(new SubmitDecision(side, decision), SubmitTimeout);
        }
        catch (AskTimeoutException)
        {
            // The actor stops once the battle ends, before this session may have seen the last update.
            return new SubmitOutcome(false, "The battle did not answer in time.", null);
        }

        return reply switch
        {
            DecisionAccepted => new SubmitOutcome(true, null, null),
            DecisionRejected rejected => new SubmitOutcome(false, rejected.Reason, rejected.Request),
            _ => throw new InvalidOperationException($"Unexpected reply {reply.GetType().Name}."),
        };
    }

    private Task OnUpdate(SideId side, BattleUpdate update)
    {
        lock (_gate)
        {
            _events[side].AddRange(update.Events);
            _requests[side] = update.Request;
            if (update.Events.Any(e => e is BattleEnded))
            {
                Status = BattleStatus.Over;
            }
        }

        return _notifier.NotifyAsync(Id, side, update);
    }

    private static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
