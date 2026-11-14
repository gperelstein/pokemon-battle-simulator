using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Battle engine modelled as a resumable state machine.
/// <para>
/// A turn is resolved by draining an ordered action queue. Whenever an action needs new input
/// (for example a self-switch move such as Volt Switch), the engine records a
/// <see cref="DecisionRequest"/> for the affected player only and pauses with the queue intact.
/// <see cref="Submit"/> resumes the queue exactly where it stopped.
/// </para>
/// Not thread-safe: callers (the battle actor) must serialize access.
/// </summary>
public sealed class Battle
{
    private readonly Dictionary<SideId, Side> _sides;
    private readonly IBattleRandom _random;
    private readonly List<DecisionRequest> _pending = [];
    private readonly Dictionary<SideId, Decision> _turnDecisions = [];
    private readonly Dictionary<SideId, Decision> _replacements = [];
    private readonly Queue<BattleAction> _queue = new();
    private List<BattleEvent> _events = [];
    private bool _started;
    private bool _turnInProgress;

    private Battle(BattleFormat format, Side p1, Side p2, IBattleRandom random)
    {
        Format = format;
        _sides = new() { [SideId.P1] = p1, [SideId.P2] = p2 };
        _random = random;
    }

    public BattleFormat Format { get; }
    public int Turn { get; private set; }
    public bool IsOver { get; private set; }
    public SideId? Winner { get; private set; }
    public IReadOnlyList<DecisionRequest> PendingRequests => _pending;

    public Side GetSide(SideId id) => _sides[id];

    public static Battle Create(
        BattleFormat format,
        IReadOnlyList<PokemonSet> p1Team,
        IReadOnlyList<PokemonSet> p2Team,
        IBattleRandom random)
    {
        var slots = format.ActiveSlots();
        ValidateTeam(p1Team, slots, nameof(p1Team));
        ValidateTeam(p2Team, slots, nameof(p2Team));
        return new Battle(format, new Side(SideId.P1, p1Team, slots), new Side(SideId.P2, p2Team, slots), random);
    }

    /// <summary>Sends in the leads and opens the first turn.</summary>
    public IReadOnlyList<BattleEvent> Start()
    {
        if (_started)
        {
            throw new InvalidOperationException("The battle has already started.");
        }

        _started = true;
        _events = [];
        foreach (var side in _sides.Values)
        {
            for (var slot = 0; slot < side.Active.Count; slot++)
            {
                SwitchIn(side, slot, side.Team[slot]);
            }
        }

        Continue();
        return _events;
    }

    /// <summary>
    /// Answers the pending request of <paramref name="side"/>. Returns the events produced by
    /// resolving as much of the battle as possible before the next pause.
    /// </summary>
    public SubmitResult Submit(SideId side, Decision decision)
    {
        if (IsOver)
        {
            return SubmitResult.Reject("The battle is over.");
        }

        var request = _pending.FirstOrDefault(r => r.Side == side);
        if (request is null)
        {
            return SubmitResult.Reject($"{side} has no pending decision.");
        }

        var error = Validate(request, decision, _sides[side]);
        if (error is not null)
        {
            return SubmitResult.Reject(error);
        }

        _events = [];
        _pending.Remove(request);

        switch (request.Kind)
        {
            case DecisionKind.Turn:
                _turnDecisions[side] = decision;
                if (_pending.Count == 0)
                {
                    BuildTurnQueue();
                }
                break;

            case DecisionKind.MidTurnSwitch:
                foreach (var choice in decision.Choices.Cast<SwitchChoice>())
                {
                    SwitchIn(_sides[side], choice.Slot, _sides[side].Team[choice.TeamIndex]);
                }
                break;

            case DecisionKind.FaintReplacement:
                _replacements[side] = decision;
                if (_pending.Count == 0)
                {
                    PerformReplacements();
                }
                break;
        }

        if (_pending.Count == 0)
        {
            Continue();
        }

        return SubmitResult.Ok(_events);
    }

    /// <summary>Runs the battle forward until it needs input from a player or ends.</summary>
    private void Continue()
    {
        while (!IsOver && _pending.Count == 0)
        {
            if (_queue.Count > 0)
            {
                Execute(_queue.Dequeue());
                CheckForWinner();
                continue;
            }

            if (_turnInProgress)
            {
                _turnInProgress = false;
                RequestFaintReplacements();
                continue;
            }

            StartTurn();
        }
    }

    private void StartTurn()
    {
        Turn++;
        _events.Add(new TurnStarted(Turn));
        foreach (var side in _sides.Values)
        {
            var slots = side.LiveSlots.ToArray();
            if (slots.Length > 0)
            {
                Request(new DecisionRequest(side.Id, DecisionKind.Turn, slots));
            }
        }
    }

    private void BuildTurnQueue()
    {
        var actions = new List<(BattleAction Action, int Bracket, int Priority, int Speed, int TieBreak)>();
        foreach (var (sideId, decision) in _turnDecisions)
        {
            var side = _sides[sideId];
            foreach (var choice in decision.Choices)
            {
                var actor = side.Active[choice.Slot]!;
                var speed = actor.Set.Stats.Speed;
                var tieBreak = _random.Next(0, int.MaxValue);
                actions.Add(choice switch
                {
                    SwitchChoice s => (new SwitchAction(side, s.Slot, actor, s.TeamIndex), 1, 0, speed, tieBreak),
                    MoveChoice m => (new MoveAction(side, m.Slot, actor, m), 0,
                        actor.Set.Moves[m.MoveIndex].Priority, speed, tieBreak),
                    _ => throw new InvalidOperationException($"Unknown choice {choice}."),
                });
            }
        }

        // Switches always go first; then priority bracket, then speed, then a random tie-break.
        foreach (var entry in actions
                     .OrderByDescending(a => a.Bracket)
                     .ThenByDescending(a => a.Priority)
                     .ThenByDescending(a => a.Speed)
                     .ThenByDescending(a => a.TieBreak))
        {
            _queue.Enqueue(entry.Action);
        }

        _turnDecisions.Clear();
        _turnInProgress = true;
    }

    private void Execute(BattleAction action)
    {
        // An actor that fainted or left the field before its turn loses its action.
        if (action.Actor.IsFainted || action.Side.Active[action.Slot] != action.Actor)
        {
            return;
        }

        switch (action)
        {
            case SwitchAction s:
                var incoming = s.Side.Team[s.TeamIndex];
                if (s.Side.Bench.Contains(incoming))
                {
                    SwitchIn(s.Side, s.Slot, incoming);
                }
                break;

            case MoveAction m:
                UseMove(m);
                break;
        }
    }

    private void UseMove(MoveAction action)
    {
        var user = action.Actor;
        var move = user.Set.Moves[action.Choice.MoveIndex];
        _events.Add(new MoveUsed(action.Side.Id, action.Slot, user.Set.Species, move.Id));

        var targets = ResolveTargets(move, action);
        if (targets is { Count: 0 })
        {
            _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "No target."));
            return;
        }

        foreach (var (side, slot) in targets ?? [])
        {
            var target = side.Active[slot]!;
            var damage = CalculateDamage(user, target, move, spread: targets!.Count > 1);
            if (damage == 0)
            {
                continue;
            }

            var dealt = target.TakeDamage(damage);
            _events.Add(new DamageDealt(side.Id, slot, target.Set.Species, dealt, target.CurrentHp));
            if (target.IsFainted)
            {
                _events.Add(new Fainted(side.Id, slot, target.Set.Species));
            }
        }

        if (move.SelfSwitch && !user.IsFainted && action.Side.Bench.Count > 0)
        {
            Request(new DecisionRequest(action.Side.Id, DecisionKind.MidTurnSwitch, [action.Slot]));
        }
    }

    /// <summary>
    /// Pokémon hit by the move, or null when the move does not target Pokémon on the field
    /// (self, ally side, field effects...).
    /// </summary>
    private IReadOnlyList<(Side Side, int Slot)>? ResolveTargets(MoveDefinition move, MoveAction action)
    {
        var foes = _sides[action.Side.Id.Opponent()];
        var liveFoes = foes.LiveSlots.Select(slot => (foes, slot)).ToArray();
        var chosen = action.Choice.TargetSlot;

        return move.Target switch
        {
            MoveTarget.AllAdjacentFoes => liveFoes,
            MoveTarget.AllAdjacent => liveFoes
                .Concat(action.Side.LiveSlots.Where(slot => slot != action.Slot).Select(slot => (action.Side, slot)))
                .ToArray(),
            MoveTarget.RandomNormal => liveFoes.Length == 0 ? [] : [liveFoes[_random.Next(0, liveFoes.Length)]],
            // A single-target move retargets to another foe when its chosen target is gone.
            MoveTarget.AdjacentFoe or MoveTarget.Normal or MoveTarget.Any or MoveTarget.Scripted =>
                liveFoes.Any(t => t.slot == chosen) ? [(foes, chosen)] : liveFoes.Take(1).ToArray(),
            _ => null,
        };
    }

    /// <summary>
    /// Core Gen 6 damage formula with spread, random roll and STAB. Type effectiveness, critical
    /// hits and the remaining modifiers are not applied yet.
    /// </summary>
    private int CalculateDamage(BattlePokemon attacker, BattlePokemon defender, MoveDefinition move, bool spread)
    {
        if (move.Category == MoveCategory.Status)
        {
            return 0;
        }

        var physical = move.Category == MoveCategory.Physical;
        var attack = physical ? attacker.Set.Stats.Attack : attacker.Set.Stats.SpecialAttack;
        var defense = physical ? defender.Set.Stats.Defense : defender.Set.Stats.SpecialDefense;

        var damage = (2 * attacker.Set.Level / 5 + 2) * move.BasePower * attack / Math.Max(1, defense) / 50 + 2;
        if (spread)
        {
            damage = damage * 3 / 4;
        }

        damage = damage * _random.Next(85, 101) / 100;
        if (attacker.Set.Types.Contains(move.Type))
        {
            damage = damage * 3 / 2;
        }

        return Math.Max(1, damage);
    }

    private void RequestFaintReplacements()
    {
        foreach (var side in _sides.Values)
        {
            var emptySlots = Enumerable.Range(0, side.Active.Count)
                .Where(slot => side.Active[slot] is null or { IsFainted: true })
                .Take(side.Bench.Count)
                .ToArray();
            if (emptySlots.Length > 0)
            {
                Request(new DecisionRequest(side.Id, DecisionKind.FaintReplacement, emptySlots));
            }
        }
    }

    private void PerformReplacements()
    {
        var switches = _replacements
            .SelectMany(entry => entry.Value.Choices.Cast<SwitchChoice>()
                .Select(choice => (Side: _sides[entry.Key], choice.Slot, Incoming: _sides[entry.Key].Team[choice.TeamIndex])))
            .OrderByDescending(s => s.Incoming.Set.Stats.Speed)
            .ToArray();

        foreach (var (side, slot, incoming) in switches)
        {
            SwitchIn(side, slot, incoming);
        }

        _replacements.Clear();
    }

    private void SwitchIn(Side side, int slot, BattlePokemon incoming)
    {
        var current = side.Active[slot];
        if (current is { IsFainted: false })
        {
            _events.Add(new SwitchedOut(side.Id, slot, current.TeamIndex, current.Set.Species));
        }

        side.Place(slot, incoming);
        _events.Add(new SwitchedIn(side.Id, slot, incoming.TeamIndex, incoming.Set.Species));
    }

    private void Request(DecisionRequest request)
    {
        _pending.Add(request);
        _events.Add(new DecisionRequested(request));
    }

    private void CheckForWinner()
    {
        var p1Lost = _sides[SideId.P1].HasLost;
        var p2Lost = _sides[SideId.P2].HasLost;
        if (!p1Lost && !p2Lost)
        {
            return;
        }

        IsOver = true;
        Winner = p1Lost && p2Lost ? null : p1Lost ? SideId.P2 : SideId.P1;
        _pending.Clear();
        _queue.Clear();
        _events.Add(new BattleEnded(Winner));
    }

    private static string? Validate(DecisionRequest request, Decision decision, Side side)
    {
        var chosenSlots = decision.Choices.Select(c => c.Slot).Order();
        if (!chosenSlots.SequenceEqual(request.Slots.Order()))
        {
            return $"Expected exactly one choice for each slot: {string.Join(", ", request.Slots)}.";
        }

        if (request.Kind != DecisionKind.Turn && decision.Choices.Any(c => c is not SwitchChoice))
        {
            return $"Only switches are allowed for {request.Kind}.";
        }

        foreach (var choice in decision.Choices)
        {
            switch (choice)
            {
                case MoveChoice move:
                    var moves = side.Active[move.Slot]!.Set.Moves;
                    if (move.MoveIndex < 0 || move.MoveIndex >= moves.Count)
                    {
                        return $"Move {move.MoveIndex} does not exist.";
                    }

                    if (move.TargetSlot < 0 || move.TargetSlot >= side.Active.Count)
                    {
                        return $"Target slot {move.TargetSlot} does not exist.";
                    }
                    break;

                case SwitchChoice sw:
                    if (sw.TeamIndex < 0 || sw.TeamIndex >= side.Team.Count || !side.Bench.Contains(side.Team[sw.TeamIndex]))
                    {
                        return $"Team member {sw.TeamIndex} cannot be switched in.";
                    }
                    break;
            }
        }

        var repeated = decision.Choices.OfType<SwitchChoice>()
            .GroupBy(s => s.TeamIndex)
            .FirstOrDefault(g => g.Count() > 1);
        return repeated is null ? null : $"Team member {repeated.Key} is chosen more than once.";
    }

    private static void ValidateTeam(IReadOnlyList<PokemonSet> team, int activeSlots, string paramName)
    {
        if (team.Count < activeSlots || team.Count > 6)
        {
            throw new ArgumentException($"A team must have between {activeSlots} and 6 Pokémon.", paramName);
        }

        if (team.Any(p => p.Moves.Count is 0 or > PokemonSet.MaxMoves))
        {
            throw new ArgumentException($"Every Pokémon must know between 1 and {PokemonSet.MaxMoves} moves.", paramName);
        }
    }

    private abstract record BattleAction(Side Side, int Slot, BattlePokemon Actor);

    private sealed record SwitchAction(Side Side, int Slot, BattlePokemon Actor, int TeamIndex)
        : BattleAction(Side, Slot, Actor);

    private sealed record MoveAction(Side Side, int Slot, BattlePokemon Actor, MoveChoice Choice)
        : BattleAction(Side, Slot, Actor);
}
