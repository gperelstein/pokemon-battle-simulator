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
public sealed partial class Battle
{
    private readonly Dictionary<SideId, Side> _sides;
    private readonly IBattleRandom _random;
    private readonly ITypeChart _typeChart;
    private readonly BattleField _field;
    private readonly DamageCalculator _damage;
    private readonly List<DecisionRequest> _pending = [];
    private readonly Dictionary<SideId, Decision> _turnDecisions = [];
    private readonly Dictionary<SideId, Decision> _replacements = [];
    private readonly Queue<BattleAction> _queue = new();
    private List<BattleEvent> _events = [];
    private bool _started;
    private bool _turnInProgress;

    private Battle(BattleFormat format, Side p1, Side p2, ITypeChart typeChart, IBattleRandom random, BattleField field)
    {
        Format = format;
        _sides = new() { [SideId.P1] = p1, [SideId.P2] = p2 };
        _random = random;
        _typeChart = typeChart;
        _field = field;
        _damage = new DamageCalculator(typeChart);
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
        ITypeChart typeChart,
        IBattleRandom random)
    {
        var slots = format.ActiveSlots();
        ValidateTeam(p1Team, slots, nameof(p1Team));
        ValidateTeam(p2Team, slots, nameof(p2Team));
        var field = new BattleField();
        return new Battle(
            format, new Side(SideId.P1, p1Team, slots, field), new Side(SideId.P2, p2Team, slots, field), typeChart, random, field);
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
                ApplyResidualDamage();
                CheckForWinner();
                if (!IsOver)
                {
                    RequestFaintReplacements();
                }

                continue;
            }

            StartTurn();
        }
    }

    private void StartTurn()
    {
        Turn++;
        _events.Add(new TurnStarted(Turn));
        // Slots locked into their next action (second turn of a charge move, recharging) are not
        // asked for a decision; when nobody has a choice to make, the turn runs on its own.
        foreach (var side in _sides.Values)
        {
            var slots = side.LiveSlots.Where(slot => !side.Active[slot]!.IsLocked).ToArray();
            if (slots.Length > 0)
            {
                Request(new DecisionRequest(side.Id, DecisionKind.Turn, slots));
            }
        }

        if (_pending.Count == 0)
        {
            BuildTurnQueue();
        }
    }

    private void BuildTurnQueue()
    {
        // Turn-scoped memory (hits taken, newly switched) used by Counter, Payback, Assurance...
        foreach (var pokemon in _sides.Values.SelectMany(s => s.Team))
        {
            pokemon.ResetTurnState();
        }

        var chosen = _turnDecisions.SelectMany(entry => entry.Value.Choices.Select(choice => (Side: _sides[entry.Key], Choice: choice)));
        var locked = _sides.Values.SelectMany(side => side.LiveSlots
            .Select(slot => side.Active[slot]!)
            .Where(actor => actor.IsLocked)
            .Select(actor => (Side: side, Choice: LockedChoice(side, actor))));

        var actions = new List<(BattleAction Action, int Bracket, int Priority, int Speed, int TieBreak)>();
        foreach (var (side, choice) in chosen.Concat(locked))
        {
            var actor = side.Active[choice.Slot]!;
            var speed = actor.Speed;
            var tieBreak = _random.Next(0, int.MaxValue);
            actions.Add(choice switch
            {
                SwitchChoice s => (new SwitchAction(side, s.Slot, actor, s.TeamIndex), 1, 0, speed, tieBreak),
                // Recharging is a move-bracket action with priority 0, like Showdown's "recharge".
                RechargeChoice r => (new RechargeAction(side, r.Slot, actor), 0, 0, speed, tieBreak),
                MoveChoice m => (new MoveAction(side, m.Slot, actor, m), 0,
                    actor.Set.Moves[m.MoveIndex].Priority, speed, tieBreak),
                _ => throw new InvalidOperationException($"Unknown choice {choice}."),
            });
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

            case RechargeAction r:
                r.Actor.MustRecharge = false;
                _events.Add(new Recharging(r.Side.Id, r.Slot, r.Actor.Set.Species));
                break;
        }
    }

    private void UseMove(MoveAction action)
    {
        var user = action.Actor;
        var move = user.Set.Moves[action.Choice.MoveIndex];
        // Second turn of a two-turn move: the charge ends whether or not the attack happens (an
        // aborted attack, e.g. by full paralysis, cancels it as in Showdown's onMoveAborted).
        var releasingCharge = user.ChargingMoveId == move.Id;
        var canMove = CanMove(action, move);
        if (releasingCharge)
        {
            user.StopCharging();
        }

        if (!canMove)
        {
            return;
        }

        var (rolled, magnitude, heals, typeBoosted) = ModifyMove(move, user);
        _events.Add(new MoveUsed(action.Side.Id, action.Slot, user.Set.Species, move.Id));
        if (magnitude is { } strength)
        {
            _events.Add(new MagnitudeActivated(action.Side.Id, action.Slot, strength));
        }

        if (!IsSupported(move))
        {
            _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "Variable base power is not supported yet."));
            return;
        }

        if (move.Charge && !releasingCharge && TryStartCharging(action, move))
        {
            return;
        }

        int? retaliation = null;
        var targets = ResolveTargets(move, action);
        if (IsRetaliation(move))
        {
            if (Retaliation(move, user) is not { } answer)
            {
                _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "There is no damage to return."));
                return;
            }

            // Aimed at the slot of the foe that hit the user, whatever target was chosen.
            retaliation = answer.Damage;
            var foeSide = _sides[answer.Hit.Side];
            if (foeSide.Active[answer.Hit.Slot] is { IsFainted: false })
            {
                targets = [(foeSide, answer.Hit.Slot)];
            }
        }

        if (targets is { Count: 0 })
        {
            _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "No target."));
            return;
        }

        // Moves without Pokémon targets: weather is supported; other field and side effects are not yet.
        if (targets is null)
        {
            if (move.SetsWeather is { } weather && !SetWeather(weather))
            {
                _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "That weather is already active."));
                return;
            }

            RequestSelfSwitch(action, move);
            return;
        }

        var hit = targets
            .Where(t => Hit(user, t.Side, t.Slot, new MoveUse(rolled, targets.Count > 1, heals, retaliation, typeBoosted)))
            .Select(t => t.Side.Active[t.Slot]!)
            .ToArray();

        // Final Gambit: the user faints once it has dealt its HP as damage.
        if (move.Id == "finalgambit" && hit.Length > 0 && !user.IsFainted)
        {
            user.TakeDamage(user.CurrentHp);
            _events.Add(new Fainted(action.Side.Id, action.Slot, user.Set.Species));
        }

        // Same order as Showdown: primary boosts and status, then the user's own changes, then
        // secondary effects (target boosts, status, user boosts).
        foreach (var target in hit.Where(t => !t.IsFainted))
        {
            ChangeStages(target, rolled.Boosts, announceLimit: true);
            if (HealAmount(move, target) is { } amount)
            {
                Heal(target, amount, user, move);
            }

            if (move.Status is { } status)
            {
                TrySetStatus(target, status, action, move, primary: true);
            }

            if (CuredOnHit(move) is { } cured && target.Status == cured)
            {
                Cure(target);
            }
        }

        if (hit.Length > 0 && move.SelfBoosts.Count > 0 && RollChance(move.SelfBoostChance ?? 100))
        {
            ChangeStages(user, move.SelfBoosts, announceLimit: false);
        }

        foreach (var target in hit)
        {
            foreach (var secondary in move.Secondaries.Where(s => RollChance(s.Chance)))
            {
                ChangeStages(target, secondary.TargetBoosts, announceLimit: false);
                if (secondary.Status is { } status)
                {
                    TrySetStatus(target, status, action, move, primary: false);
                }

                ChangeStages(user, secondary.SelfBoosts, announceLimit: false);
            }

            if (move.ThawsTarget && target.Status == MajorStatus.Freeze && !target.IsFainted)
            {
                Cure(target);
            }
        }

        // Hyper Beam and similar moves need a recharge turn whenever they hit, even on a KO (Gen 2+).
        if (move.Recharge && hit.Length > 0 && !user.IsFainted)
        {
            user.MustRecharge = true;
        }

        // A self-switch move that did not hit anything (immune targets, misses) does not switch out.
        if (hit.Length > 0)
        {
            RequestSelfSwitch(action, move);
        }
    }

    private void RequestSelfSwitch(MoveAction action, MoveDefinition move)
    {
        if (move.SelfSwitch && !action.Actor.IsFainted && action.Side.Bench.Count > 0)
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
            MoveTarget.Self => [(action.Side, action.Slot)],
            // A single-target move retargets to another foe when its chosen target is gone.
            MoveTarget.AdjacentFoe or MoveTarget.Normal or MoveTarget.Any or MoveTarget.Scripted =>
                liveFoes.Any(t => t.slot == chosen) ? [(foes, chosen)] : liveFoes.Take(1).ToArray(),
            _ => null,
        };
    }

    /// <summary>
    /// Status checks before the move is announced, in Showdown's priority order: sleep and freeze,
    /// then paralysis. Returns false when the Pokémon loses its action.
    /// </summary>
    private bool CanMove(MoveAction action, MoveDefinition move)
    {
        var user = action.Actor;
        switch (user.Status)
        {
            case MajorStatus.Sleep:
                if (--user.SleepTurns <= 0)
                {
                    Cure(user);
                    return true;
                }

                _events.Add(new CantMove(action.Side.Id, action.Slot, user.Set.Species, MajorStatus.Sleep));
                return move.SleepUsable;

            case MajorStatus.Freeze:
                // Defrosting moves thaw their user; otherwise there is a 1 in 5 chance to thaw.
                if (move.Defrost || _random.Next(0, 5) == 0)
                {
                    Cure(user);
                    return true;
                }

                _events.Add(new CantMove(action.Side.Id, action.Slot, user.Set.Species, MajorStatus.Freeze));
                return false;

            case MajorStatus.Paralysis when _random.Next(0, 4) == 0:
                _events.Add(new CantMove(action.Side.Id, action.Slot, user.Set.Species, MajorStatus.Paralysis));
                return false;

            default:
                return true;
        }
    }

    /// <summary>
    /// Runs the hit checks of a move against one target, in Gen 6 order (type immunity, powder and
    /// move-specific immunities, then accuracy), and deals the damage. Returns false when the target
    /// is immune or the move misses.
    /// </summary>
    private bool Hit(BattlePokemon user, Side side, int slot, MoveUse use)
    {
        var (move, spread, heals, retaliation) = (use.Move, use.Spread, use.Heals, use.Retaliation);
        var target = side.Active[slot]!;
        // Semi-invulnerable targets (Fly, Dig...) are checked first: unreachable moves miss without a
        // roll. No Guard on either side reaches them (its onAnyInvulnerability).
        if (target != user && !HasNoGuard(user, target) && IsOutOfReach(target, move))
        {
            _events.Add(new Missed(side.Id, slot, target.Set.Species));
            return false;
        }

        var damaging = move.Category != MoveCategory.Status;
        var scrappy = user.Set.Ability == "scrappy" && move.Type is PokemonType.Normal or PokemonType.Fighting;
        var typeImmune = !move.IgnoresTypeImmunity && !scrappy &&
            _typeChart.Effectiveness(move.Type, target.Set.Types) == 0;
        var powderImmune = move.Powder && target != user && _typeChart.IsImmune(target.Set.Types, TypeCondition.Powder);
        // Endeavor needs a target with more HP; OHKO moves cannot affect a higher-level target.
        var moveImmune = (move.Id == "endeavor" && user.CurrentHp >= target.CurrentHp) ||
            (move.Ohko && user.Set.Level < target.Set.Level);
        if (typeImmune || powderImmune || moveImmune)
        {
            _events.Add(new Immune(side.Id, slot, target.Set.Species));
            return false;
        }

        if (!RollAccuracy(user, target, move))
        {
            _events.Add(new Missed(side.Id, slot, target.Set.Species));
            return false;
        }

        if (!damaging)
        {
            return true;
        }

        if (heals)
        {
            // Present: Math.round(maxHp / 4).
            return Heal(target, (target.Set.Stats.Hp + 2) / 4, user, move);
        }

        var physical = move.Category == MoveCategory.Physical;
        var result = move.Ohko
            ? new DamageResult(target.Set.Stats.Hp, 1, false)
            : CallbackDamage(move, user, target, retaliation) is { } fixedDamage
                ? new DamageResult(fixedDamage, 1, false)
                : _damage.Calculate(
                    new DamageInput(
                        new Combatant(user.Set, user.CurrentHp, user.Status,
                            AttackStage: user.GetStage(physical ? BoostStat.Attack : BoostStat.SpecialAttack)),
                        new Combatant(target.Set, target.CurrentHp, target.Status,
                            DefenseStage: target.GetStage(physical ? BoostStat.Defense : BoostStat.SpecialDefense)),
                        move with { BasePower = BasePower(move, user, target, use.TypeBoosted) })
                    {
                        Spread = spread,
                        Weather = Weather,
                        FinalModifiers = BattleDamageModifiers(target, move),
                    },
                    _random);

        if (result.Effectiveness != 1)
        {
            _events.Add(new Effectiveness(side.Id, slot, result.Effectiveness));
        }

        if (result.Critical)
        {
            _events.Add(new CriticalHit(side.Id, slot));
        }

        var dealt = target.TakeDamage(result.Damage);
        _events.Add(new DamageDealt(side.Id, slot, target.Set.Species, dealt, target.CurrentHp));
        var (userSide, userSlot) = Locate(user);
        target.HitsTakenThisTurn.Add(new HitTaken(user, userSide.Id, userSlot, dealt, move.Category));
        target.HurtThisTurn = true;
        if (move.Ohko && target.IsFainted)
        {
            _events.Add(new OneHitKo(side.Id, slot));
        }

        if (target.IsFainted)
        {
            _events.Add(new Fainted(side.Id, slot, target.Set.Species));
        }
        else if (move.Type == PokemonType.Fire && target.Status == MajorStatus.Freeze)
        {
            Cure(target);
        }

        return true;
    }

    /// <summary>Restores HP with a move; fails when the target is already at full HP.</summary>
    private bool Heal(BattlePokemon target, int amount, BattlePokemon user, MoveDefinition move)
    {
        if (target.CurrentHp == target.Set.Stats.Hp)
        {
            var (userSide, userSlot) = Locate(user);
            _events.Add(new MoveFailed(userSide.Id, userSlot, move.Id, "The target is already at full HP."));
            return false;
        }

        var (side, slot) = Locate(target);
        var healed = target.Heal(amount);
        _events.Add(new Healed(side.Id, slot, target.Set.Species, healed, target.CurrentHp));
        return true;
    }

    private bool RollAccuracy(BattlePokemon user, BattlePokemon target, MoveDefinition move)
    {
        // Moves that never miss and status moves aimed at the user skip the check. Gen 6 (from
        // Showdown's gen7 mod): Toxic never misses when used by a Poison type.
        // No Guard on either side makes every move hit (its onAnyAccuracy returns true).
        if (move.Accuracy is not { } accuracy || move.Target == MoveTarget.Self ||
            (move.Id == "toxic" && user.Set.Types.Contains(PokemonType.Poison)) || HasNoGuard(user, target))
        {
            return true;
        }

        // One-hit KO moves: 30% plus the level difference, ignoring accuracy and evasion stages.
        if (move.Ohko)
        {
            return RollChance(30 + user.Set.Level - target.Set.Level);
        }

        // ModifyAccuracy: Wonder Skin (priority 10) replaces a status move's accuracy with 50, then
        // the chained modifiers of the target (Sand Veil, Snow Cloak) and the user (Compound Eyes, Hustle).
        var targetAbility = target.Set.Ability;
        if (targetAbility == "wonderskin" && move.Category == MoveCategory.Status)
        {
            accuracy = 50;
        }

        var chain = new ModifierChain();
        if ((targetAbility == "sandveil" && Weather == Battles.Weather.Sandstorm) ||
            (targetAbility == "snowcloak" && Weather == Battles.Weather.Hail))
        {
            chain = chain.Chain(3277, 4096);
        }

        if (user.Set.Ability == "compoundeyes")
        {
            chain = chain.Chain(5325, 4096);
        }
        else if (user.Set.Ability == "hustle" && move.Category == MoveCategory.Physical)
        {
            chain = chain.Chain(3277, 4096);
        }

        // Stages: Unaware ignores the other side's; Keen Eye ignores the target's evasion.
        var accuracyStage = targetAbility == "unaware" ? 0 : user.GetStage(BoostStat.Accuracy);
        var ignoresEvasion = move.IgnoreEvasion || user.Set.Ability is "keeneye" or "unaware";
        var evasion = ignoresEvasion ? 0 : target.GetStage(BoostStat.Evasion);
        return RollChance(StatStages.ApplyToAccuracy(chain.Apply(accuracy), accuracyStage, evasion));
    }

    private static bool HasNoGuard(BattlePokemon user, BattlePokemon target) =>
        user.Set.Ability == "noguard" || target.Set.Ability == "noguard";

    /// <summary>
    /// Inflicts a major status unless the target already has one or its type is immune. Only a
    /// move's primary status reports the failure; secondary statuses fail silently.
    /// </summary>
    private void TrySetStatus(BattlePokemon target, MajorStatus status, MoveAction action, MoveDefinition move, bool primary)
    {
        if (target.IsFainted)
        {
            return;
        }

        if (target.Status is not null)
        {
            if (primary)
            {
                _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "The target already has a status."));
            }

            return;
        }

        var (side, slot) = Locate(target);
        // Sunny Day grants immunity to freezing (Showdown's sunnyday onImmunity).
        var sunBlocksFreeze = status == MajorStatus.Freeze && Weather == Battles.Weather.Sun;
        if (sunBlocksFreeze ||
            (ImmunityTo(status) is { } condition && _typeChart.IsImmune(target.Set.Types, condition)))
        {
            if (primary)
            {
                _events.Add(new Immune(side.Id, slot, target.Set.Species));
            }

            return;
        }

        target.SetStatus(status);
        if (status == MajorStatus.Sleep)
        {
            // Showdown draws 2-4: the counter drops on each move attempt, so 1-3 turns asleep.
            target.SleepTurns = _random.Next(2, 5);
        }

        _events.Add(new StatusInflicted(side.Id, slot, target.Set.Species, status));
    }

    private static TypeCondition? ImmunityTo(MajorStatus status) => status switch
    {
        MajorStatus.Burn => TypeCondition.Burn,
        MajorStatus.Paralysis => TypeCondition.Paralysis,
        MajorStatus.Poison or MajorStatus.Toxic => TypeCondition.Poison,
        MajorStatus.Freeze => TypeCondition.Freeze,
        _ => null,
    };

    private void Cure(BattlePokemon pokemon)
    {
        var (side, slot) = Locate(pokemon);
        _events.Add(new StatusCured(side.Id, slot, pokemon.Set.Species, pokemon.Status!.Value));
        pokemon.CureStatus();
    }

    /// <summary>
    /// End-of-turn damage: weather first (residual order 1), then poison (order 9) before burn
    /// (order 10), each by descending speed. Gen 6 burn deals 1/8 of max HP.
    /// </summary>
    private void ApplyResidualDamage()
    {
        ApplyWeatherResidual();
        var hurt = _sides.Values
            .SelectMany(side => side.LiveSlots.Select(slot => side.Active[slot]!))
            .Where(p => p.Status is MajorStatus.Burn or MajorStatus.Poison or MajorStatus.Toxic)
            .OrderBy(p => p.Status == MajorStatus.Burn ? 1 : 0)
            .ThenByDescending(p => p.Speed)
            .ToArray();

        foreach (var pokemon in hurt)
        {
            var maxHp = pokemon.Set.Stats.Hp;
            int damage;
            if (pokemon.Status == MajorStatus.Toxic)
            {
                pokemon.ToxicStage = Math.Min(pokemon.ToxicStage + 1, 15);
                // trunc(max(1, maxHp / 16) * stage): the fraction is kept before multiplying.
                damage = maxHp < 16 ? pokemon.ToxicStage : maxHp * pokemon.ToxicStage / 16;
            }
            else
            {
                damage = Math.Max(1, maxHp / 8);
            }

            var (side, slot) = Locate(pokemon);
            var dealt = pokemon.TakeDamage(damage);
            pokemon.HurtThisTurn = true;
            _events.Add(new StatusDamage(side.Id, slot, pokemon.Set.Species, pokemon.Status!.Value, dealt, pokemon.CurrentHp));
            if (pokemon.IsFainted)
            {
                _events.Add(new Fainted(side.Id, slot, pokemon.Set.Species));
            }
        }
    }

    private (Side Side, int Slot) Locate(BattlePokemon pokemon)
    {
        var side = _sides[pokemon.Side];
        return (side, side.Active.ToList().IndexOf(pokemon));
    }

    /// <summary>Percentage check; chances of 100 or more always succeed without rolling.</summary>
    private bool RollChance(int percent) => percent >= 100 || _random.Next(0, 100) < percent;

    /// <summary>
    /// Applies stage changes to an active Pokémon. Only a move's primary effect announces that a stat
    /// cannot change any further; self and secondary changes stay silent, as in Showdown.
    /// </summary>
    private void ChangeStages(BattlePokemon pokemon, IReadOnlyList<StatChange> changes, bool announceLimit)
    {
        if (pokemon.IsFainted || changes.Count == 0)
        {
            return;
        }

        var (side, slot) = Locate(pokemon);
        foreach (var change in changes)
        {
            var delta = pokemon.ChangeStage(change.Stat, change.Stages);
            if (delta != 0)
            {
                _events.Add(new StatStageChanged(
                    side.Id, slot, pokemon.Set.Species, change.Stat, delta, pokemon.GetStage(change.Stat)));
            }
            else if (announceLimit)
            {
                _events.Add(new StatStageAtLimit(side.Id, slot, pokemon.Set.Species, change.Stat, change.Stages > 0));
            }
        }
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
            .OrderByDescending(s => s.Incoming.Speed)
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

        // Stat stages and other volatile state are lost on leaving the field (fainted Pokémon included).
        current?.LeaveField();
        incoming.ToxicStage = 0;
        incoming.NewlySwitched = true;
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

    /// <summary>One use of a move against its targets, after the changes made before announcing it.</summary>
    /// <param name="Heals">Present rolled its heal: the target recovers HP instead of taking damage.</param>
    /// <param name="Retaliation">Damage returned by Counter, Mirror Coat or Metal Burst.</param>
    /// <param name="TypeBoosted">Aerilate changed the move's type.</param>
    private sealed record MoveUse(MoveDefinition Move, bool Spread, bool Heals, int? Retaliation, bool TypeBoosted);

    private sealed record RechargeAction(Side Side, int Slot, BattlePokemon Actor) : BattleAction(Side, Slot, Actor);

    /// <summary>Engine-made choice for a Pokémon that must recharge; players never send it.</summary>
    private sealed record RechargeChoice(int Slot) : SlotChoice(Slot);

    /// <summary>The action a locked Pokémon takes without asking its player.</summary>
    private static SlotChoice LockedChoice(Side side, BattlePokemon actor)
    {
        var slot = side.Active.ToList().IndexOf(actor);
        return actor.MustRecharge ? new RechargeChoice(slot) : actor.ChargedChoice! with { Slot = slot };
    }
}
