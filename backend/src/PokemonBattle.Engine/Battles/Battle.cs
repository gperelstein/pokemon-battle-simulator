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
        _field.WeatherSuppressed = CloudNineActive;
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
                SwitchIn(side, slot, side.Team[slot], runAbilities: false);
            }
        }

        // Every lead is on the field before their abilities activate, fastest first.
        RunSwitchInAbilities(_sides.Values.SelectMany(side => side.Active).OfType<BattlePokemon>());
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
                UpdateAbilities();
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
                var trapped = slots.Where(slot => IsTrapped(side.Active[slot]!)).ToArray();
                Request(new DecisionRequest(side.Id, DecisionKind.Turn, slots, trapped, DisabledMoves(side, slots)));
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

        var actions = new List<(BattleAction Action, int Bracket, int Priority, int Fraction, int Speed, int TieBreak)>();
        foreach (var (side, choice) in chosen.Concat(locked))
        {
            var actor = side.Active[choice.Slot]!;
            var speed = actor.Speed;
            // Move actions (recharging included) get their item's fractional priority (Quick Claw...).
            var fraction = choice is SwitchChoice ? 0 : FractionalPriority(side, choice.Slot, actor);
            var tieBreak = _random.Next(0, int.MaxValue);
            actions.Add(choice switch
            {
                SwitchChoice s => (new SwitchAction(side, s.Slot, actor, s.TeamIndex), 1, 0, 0, speed, tieBreak),
                // Recharging is a move-bracket action with priority 0, like Showdown's "recharge".
                RechargeChoice r => (new RechargeAction(side, r.Slot, actor), 0, 0, fraction, speed, tieBreak),
                MoveChoice m => (new MoveAction(side, m.Slot, actor, m), 0,
                    actor.Set.Moves[m.MoveIndex].Priority, fraction, speed, tieBreak),
                _ => throw new InvalidOperationException($"Unknown choice {choice}."),
            });
        }

        // Switches always go first; then priority bracket, fractional priority (items), speed and a
        // random tie-break.
        foreach (var entry in actions
                     .OrderByDescending(a => a.Bracket)
                     .ThenByDescending(a => a.Priority)
                     .ThenByDescending(a => a.Fraction)
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

        // A Choice item locks the move in once it is used (its onModifyMove).
        LockChoice(user, move);
        ClearSwitchFlags();

        // Mold Breaker sets ignoreAbility while its move runs (Showdown's onModifyMove).
        _moldBreakerUser = user.Ability == "moldbreaker" ? user : null;
        try
        {
            RunMove(action, move, releasingCharge);
        }
        finally
        {
            _moldBreakerUser = null;
        }

        // White Herb's onAnyAfterMove: any holder whose stats the move lowered restores them now.
        RestoreLoweredStagesOfActivePokemon();

        // End of the action (runAction): forced switches first, then a mid-turn switch request.
        ResolveSwitchFlags();
    }

    private void RunMove(MoveAction action, MoveDefinition move, bool releasingCharge)
    {
        var user = action.Actor;
        _criticalTargets.Clear();
        _damagedTargets.Clear();
        _superEffectiveTargets.Clear();
        _moveDamageDealt = 0;
        _gemUser = null;
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

        if (StoppedByDamp(action, move))
        {
            return;
        }

        CountMetronomeUse(user, move, releasingCharge);

        // Explosion and Self-Destruct: the user faints before hitting, even if the move then misses.
        // Its faint is reported once the move has been resolved, like Showdown's faint queue.
        var userFaints = move.SelfDestruct == SelfDestruct.Always;
        if (userFaints)
        {
            user.TakeDamage(user.CurrentHp);
        }

        int? retaliation = null;
        var targets = ResolveTargets(move, action);
        if (targets is { Count: > 0 } && !IsRetaliation(move))
        {
            targets = RedirectToLightningRod(move, user, targets);
        }

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
            if (userFaints)
            {
                _events.Add(new Fainted(action.Side.Id, action.Slot, user.Set.Species));
            }

            return;
        }

        // Moves without Pokémon targets: weather is supported; other field and side effects are not yet.
        if (targets is null)
        {
            if (move.SetsWeather is { } weather && !SetWeather(weather, user))
            {
                _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "That weather is already active."));
                return;
            }

            user.MoveSucceededThisTurn = true;
            RequestSelfSwitch(action, move);
            return;
        }

        var hit = targets
            .Where(t => Hit(user, t.Side, t.Slot, new MoveUse(rolled, targets.Count > 1, heals, retaliation, typeBoosted)))
            .Select(t => t.Side.Active[t.Slot]!)
            .ToArray();
        hit = WithoutFailedDrags(action, move, hit);
        user.MoveSucceededThisTurn = hit.Length > 0;

        // Final Gambit and Memento: the user faints once the move has hit.
        if (move.SelfDestruct == SelfDestruct.IfHit && hit.Length > 0 && !user.IsFainted)
        {
            user.TakeDamage(user.CurrentHp);
            userFaints = true;
        }

        if (userFaints)
        {
            _events.Add(new Fainted(action.Side.Id, action.Slot, user.Set.Species));
        }

        // Same order as Showdown: primary boosts and status, then the user's own changes, then
        // secondary effects (target boosts, status, user boosts).
        foreach (var target in hit.Where(t => !t.IsFainted))
        {
            ChangeStages(target, rolled.Boosts, announceLimit: true, source: user);
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

            // Anger Point (onHit): a critical hit maximizes its holder's Attack.
            if (_criticalTargets.Contains(target) && AbilityOf(target) == "angerpoint")
            {
                ChangeStages(target, [new StatChange(BoostStat.Attack, 12)], announceLimit: false, source: target,
                    fromAbility: "angerpoint");
            }
        }

        // Sticky Barb's onHit (after the primary effects), including from a holder just knocked out.
        foreach (var target in hit)
        {
            PassStickyBarb(user, target, move);
        }

        // Sheer Force drops the secondary effects (and the user's own changes) of moves that have
        // any; Serene Grace doubles their chances.
        var sheerForce = user.Ability == "sheerforce" && move.HasAnySecondary;
        var chanceFactor = user.Ability == "serenegrace" ? 2 : 1;
        if (hit.Length > 0 && !sheerForce && move.SelfBoosts.Count > 0 &&
            RollChance((move.SelfBoostChance ?? 100) * (move.SelfBoostChance is null ? 1 : chanceFactor)))
        {
            ChangeStages(user, move.SelfBoosts, announceLimit: false);
        }

        foreach (var target in hit)
        {
            // Shield Dust keeps only the secondaries that affect the attacker (onModifySecondaries).
            var secondaries = sheerForce ? []
                : AbilityOf(target) == "shielddust" ? move.Secondaries.Where(s => s.SelfBoosts.Count > 0)
                : move.Secondaries;
            foreach (var secondary in secondaries.Where(s => RollChance(s.Chance * chanceFactor)))
            {
                ChangeStages(target, secondary.TargetBoosts, announceLimit: false, source: user);
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

        // Force switch (step 6, after the secondaries and before DamagingHit).
        FlagForcedSwitches(user, move, hit);

        // DamagingHit, in Showdown's left-to-right order: Aftermath (order 1) and Rocky Helmet (order
        // 2) for every damaged target, then each target's other ability and item reactions and the
        // attacker's Poison Touch.
        var damaged = hit.Where(_damagedTargets.Contains).ToArray();
        foreach (var target in damaged)
        {
            AftermathOnDamagingHit(user, target, move);
        }

        foreach (var target in damaged)
        {
            RockyHelmetOnDamagingHit(user, target, move);
        }

        foreach (var target in damaged)
        {
            AfterDamagingHit(user, target, move, rolled.Type);
        }

        // The Update event after a move that hit something (Berry Juice).
        if (hit.Length > 0)
        {
            UpdateItems();

            // A self-switch move that did not hit anything (immune targets, misses) does not switch
            // out. Showdown sets the flag while running the move's effects, before the items below.
            RequestSelfSwitch(action, move);

            // AfterMoveSecondary (Eject Button, Red Card), skipped when Sheer Force removed the
            // move's secondaries.
            if (!sheerForce)
            {
                ItemsAfterMoveSecondary(user, move, hit);
            }
        }

        // Life Orb and Shell Bell (AfterMoveSecondarySelf) run before the faints are processed (Moxie).
        ApplyLifeOrbRecoil(user, move, hit.Length > 0, sheerForce);
        ApplyShellBell(user, sheerForce);

        // Moxie: one Attack stage per Pokémon this move knocked out, once the faints are processed.
        if (user.Ability == "moxie" && !user.IsFainted)
        {
            foreach (var _ in hit.Where(t => t.IsFainted))
            {
                ChangeStages(user, [new StatChange(BoostStat.Attack, 1)], announceLimit: false, source: user,
                    fromAbility: "moxie");
            }
        }

        // Hyper Beam and similar moves need a recharge turn whenever they hit, even on a KO (Gen 2+).
        if (move.Recharge && hit.Length > 0 && !user.IsFainted)
        {
            user.MustRecharge = true;
        }
    }

    /// <summary>
    /// A self-switch move (U-turn, Volt Switch) flags its user; the replacement is requested once the
    /// action ends (<see cref="ResolveSwitchFlags"/>), if the user is still standing on the field.
    /// </summary>
    private void RequestSelfSwitch(MoveAction action, MoveDefinition move)
    {
        if (move.SelfSwitch && !action.Actor.IsFainted && action.Side.Bench.Count > 0)
        {
            _switchFlag = new SwitchFlag(action.Actor, FromItem: false);
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
                // Early Bird makes the sleep counter drop twice as fast.
                user.SleepTurns -= user.Ability == "earlybird" ? 2 : 1;
                if (user.SleepTurns <= 0)
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

        // Gen 6 order: type immunity (Levitate included), the target's TryHit abilities, then the
        // move's own immunities, all before accuracy.
        var damaging = move.Category != MoveCategory.Status;
        var scrappy = user.Ability == "scrappy" && move.Type is PokemonType.Normal or PokemonType.Fighting;
        // Ring Target negates the immunities the holder's types give (onNegateImmunity).
        var typeImmune = !move.IgnoresTypeImmunity && !scrappy && !HeldItems.IgnoresTypeImmunities(CombatSet(target)) &&
            _typeChart.Effectiveness(move.Type, HeldItems.DefendingTypes(CombatSet(target), move.Type)) == 0;
        if (typeImmune)
        {
            _events.Add(new Immune(side.Id, slot, target.Set.Species));
            return false;
        }

        // An Iron Ball grounds its holder before Levitate is checked (isGrounded).
        if (!move.IgnoresTypeImmunity && move.Type == PokemonType.Ground && AbilityOf(target) == "levitate" &&
            target.Item != HeldItems.IronBall)
        {
            AbilityImmunity(target, "levitate");
            return false;
        }

        // isGrounded checks Air Balloon last, after Levitate.
        if (FloatsOnAirBalloon(target, move))
        {
            _events.Add(new Immune(side.Id, slot, target.Set.Species));
            return false;
        }

        if (AbsorbedByAbility(user, target, move) || BlockedBySafetyGoggles(user, target, move))
        {
            return false;
        }

        var powderImmune = move.Powder && target != user && _typeChart.IsImmune(target.Set.Types, TypeCondition.Powder);
        // Endeavor needs a target with more HP; OHKO moves cannot affect a higher-level target.
        var moveImmune = (move.Id == "endeavor" && user.CurrentHp >= target.CurrentHp) ||
            (move.Ohko && user.Set.Level < target.Set.Level);
        if (powderImmune || moveImmune)
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

        // TryPrimaryHit: a Gem is used up just before the move deals its damage (or heals).
        TryUseGem(user, target, move);

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
                        new Combatant(CombatSet(user), user.CurrentHp, user.Status,
                            AttackStage: user.GetStage(physical ? BoostStat.Attack : BoostStat.SpecialAttack),
                            FlashFire: user.FlashFireActive && AbilityOf(user) == "flashfire",
                            ConsecutiveUses: user.MetronomeCount),
                        new Combatant(CombatSet(target), target.CurrentHp, target.Status,
                            DefenseStage: target.GetStage(physical ? BoostStat.Defense : BoostStat.SpecialDefense),
                            AbilitySuppressed: AbilityOf(target) is null),
                        move with { BasePower = BasePower(move, user, target, use.TypeBoosted) })
                    {
                        Spread = spread,
                        Weather = EffectiveWeather,
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
            _criticalTargets.Add(target);
        }

        // Damage event handlers: Sturdy (priority -30), then Focus Sash / Focus Band (-40).
        var dealt = target.TakeDamage(EnduredByItem(target, EnduredBySturdy(target, result.Damage)));
        _damagedTargets.Add(target);
        _moveDamageDealt += dealt;
        if (result.Effectiveness > 1)
        {
            _superEffectiveTargets.Add(target);
        }

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
        // the chained modifiers of the target (Sand Veil, Snow Cloak) and the user (Compound Eyes,
        // Hustle) at priority -1, then the items at -2.
        var targetAbility = AbilityOf(target);
        if (targetAbility == "wonderskin" && move.Category == MoveCategory.Status)
        {
            accuracy = 50;
        }

        var chain = new ModifierChain();
        if ((targetAbility == "sandveil" && EffectiveWeather == Battles.Weather.Sandstorm) ||
            (targetAbility == "snowcloak" && EffectiveWeather == Battles.Weather.Hail))
        {
            chain = chain.Chain(3277, 4096);
        }

        if (user.Ability == "compoundeyes")
        {
            chain = chain.Chain(5325, 4096);
        }
        else if (user.Ability == "hustle" && move.Category == MoveCategory.Physical)
        {
            chain = chain.Chain(3277, 4096);
        }

        // Then the items (priority -2): Wide Lens, Zoom Lens (the target has already moved), Bright
        // Powder, Lax Incense.
        chain = ChainItemAccuracy(chain, user, target, targetMoved: !WillMove(target));

        // Stages: Unaware ignores the other side's; Keen Eye ignores the target's evasion.
        var accuracyStage = targetAbility == "unaware" ? 0 : user.GetStage(BoostStat.Accuracy);
        var ignoresEvasion = move.IgnoreEvasion || user.Ability is "keeneye" or "unaware";
        var evasion = ignoresEvasion ? 0 : target.GetStage(BoostStat.Evasion);
        return RollChance(StatStages.ApplyToAccuracy(chain.Apply(accuracy), accuracyStage, evasion));
    }

    private static bool HasNoGuard(BattlePokemon user, BattlePokemon target) =>
        user.Ability == "noguard" || target.Ability == "noguard";

    /// <summary>
    /// Inflicts a major status from a move. Only a move's primary status reports the failure;
    /// secondary statuses fail silently.
    /// </summary>
    private void TrySetStatus(BattlePokemon target, MajorStatus status, MoveAction action, MoveDefinition move, bool primary) =>
        SetStatus(target, status, action.Actor, primary ? (action, move) : null, viaAbility: null);

    /// <summary>
    /// Inflicts a major status unless the target already has one, its type is immune or its ability
    /// blocks it. Failures are reported only for a move's primary status. A status inflicted by an
    /// ability (Static...) announces that ability first. Synchronize then passes burn, paralysis and
    /// poison back to the Pokémon that caused them.
    /// </summary>
    /// <param name="viaItem">The target's own item causing the status (Toxic Orb), announced before it.</param>
    private bool SetStatus(
        BattlePokemon target,
        MajorStatus status,
        BattlePokemon? source,
        (MoveAction Action, MoveDefinition Move)? primary,
        (BattlePokemon Holder, string Ability)? viaAbility,
        string? viaItem = null)
    {
        if (target.IsFainted)
        {
            return false;
        }

        if (target.Status is not null)
        {
            if (primary is var (action, move))
            {
                _events.Add(new MoveFailed(action.Side.Id, action.Slot, move.Id, "The target already has a status."));
            }

            return false;
        }

        var (side, slot) = Locate(target);
        // Sunny Day grants immunity to freezing (Showdown's sunnyday onImmunity).
        var sunBlocksFreeze = status == MajorStatus.Freeze && EffectiveWeather == Battles.Weather.Sun;
        if (sunBlocksFreeze ||
            (ImmunityTo(status) is { } condition && _typeChart.IsImmune(target.Set.Types, condition)))
        {
            if (primary is not null)
            {
                _events.Add(new Immune(side.Id, slot, target.Set.Species));
            }

            return false;
        }

        // Abilities run after the type check (Showdown's SetStatus event); silent for secondaries.
        if (StatusBlockingAbility(target, status) is { } ability)
        {
            if (primary is not null)
            {
                AbilityImmunity(target, ability);
            }

            return false;
        }

        if (viaAbility is var (holder, holderAbility))
        {
            AbilityActivation(holder, holderAbility);
        }

        if (viaItem is not null)
        {
            ItemActivation(target, viaItem);
        }

        target.SetStatus(status);
        if (status == MajorStatus.Sleep)
        {
            // Showdown draws 2-4: the counter drops on each move attempt, so 1-3 turns asleep.
            target.SleepTurns = _random.Next(2, 5);
        }

        _events.Add(new StatusInflicted(side.Id, slot, target.Set.Species, status));

        if (source is not null && source != target && target.Ability == "synchronize" &&
            status is MajorStatus.Burn or MajorStatus.Paralysis or MajorStatus.Poison or MajorStatus.Toxic)
        {
            AbilityActivation(target, "synchronize");
            SetStatus(source, status, target, primary: null, viaAbility: null);
        }

        return true;
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
    /// End of the turn, in Showdown's residual order: weather (order 1), abilities and Leftovers /
    /// Black Sludge (order 5), poison (order 9) before burn (order 10), each by descending speed,
    /// then Sticky Barb and the status Orbs (order 28) and White Herb (order 29), and the Update
    /// event that follows the residual action (Berry Juice). Gen 6 burn deals 1/8 of max HP.
    /// </summary>
    private void ApplyResidualDamage()
    {
        var bySpeed = ActivePokemon().OrderByDescending(p => p.Speed).ToArray();
        ApplyWeatherResidual();
        ApplyAbilityResiduals();
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

            // Magic Guard blocks the damage; the toxic counter above still grows, as in Showdown.
            if (IgnoresIndirectDamage(pokemon))
            {
                continue;
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

        ApplyLateItemResiduals(bySpeed);
        UpdateItems();
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
    /// cannot change any further, or that an ability blocked a drop; self and secondary changes stay
    /// silent, as in Showdown.
    /// </summary>
    /// <param name="source">Pokémon causing the change; drops from another Pokémon can be blocked by abilities.</param>
    /// <param name="fromAbility">
    /// Ability causing the change, announced before the first stat that actually changes (Showdown's
    /// "-ability ... boost").
    /// </param>
    private void ChangeStages(
        BattlePokemon pokemon,
        IReadOnlyList<StatChange> changes,
        bool announceLimit,
        BattlePokemon? source = null,
        string? fromAbility = null)
    {
        if (pokemon.IsFainted || changes.Count == 0)
        {
            return;
        }

        changes = WithoutBlockedDrops(pokemon, source, changes, announceLimit);

        var (side, slot) = Locate(pokemon);
        foreach (var change in changes)
        {
            var delta = pokemon.ChangeStage(change.Stat, change.Stages);
            if (delta != 0)
            {
                if (fromAbility is not null)
                {
                    AbilityActivation(pokemon, fromAbility);
                    fromAbility = null;
                }

                _events.Add(new StatStageChanged(
                    side.Id, slot, pokemon.Set.Species, change.Stat, delta, pokemon.GetStage(change.Stat)));
            }
            else if (announceLimit)
            {
                _events.Add(new StatStageAtLimit(side.Id, slot, pokemon.Set.Species, change.Stat, change.Stages > 0));
            }

            // Defiant / Competitive: each stat a foe lowers is answered with +2 Attack / Sp. Atk.
            if (delta < 0 && source is not null && source.Side != pokemon.Side &&
                pokemon.Ability is "defiant" or "competitive")
            {
                var raised = pokemon.Ability == "defiant" ? BoostStat.Attack : BoostStat.SpecialAttack;
                ChangeStages(pokemon, [new StatChange(raised, 2)], announceLimit: false, source: pokemon,
                    fromAbility: pokemon.Ability);
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
            SwitchIn(side, slot, incoming, runAbilities: false);
        }

        RunSwitchInAbilities(switches.Select(s => s.Incoming));
        _replacements.Clear();
    }

    /// <param name="runAbilities">
    /// Run the newcomer's switch-in ability right away; false when several Pokémon enter together and
    /// their abilities run afterwards in speed order.
    /// </param>
    private void SwitchIn(Side side, int slot, BattlePokemon incoming, bool runAbilities = true)
    {
        var current = side.Active[slot];
        if (current is { IsFainted: false })
        {
            _events.Add(new SwitchedOut(side.Id, slot, current.TeamIndex, current.Set.Species));
            RunSwitchOutAbilities(current);
        }

        // Stat stages and other volatile state are lost on leaving the field (fainted Pokémon included).
        current?.LeaveField();
        incoming.ToxicStage = 0;
        incoming.NewlySwitched = true;
        side.Place(slot, incoming);
        _events.Add(new SwitchedIn(side.Id, slot, incoming.TeamIndex, incoming.Set.Species));
        if (runAbilities)
        {
            RunSwitchInAbilities([incoming]);
        }
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
                    var mover = side.Active[move.Slot]!;
                    var moves = mover.Set.Moves;
                    if (move.MoveIndex < 0 || move.MoveIndex >= moves.Count)
                    {
                        return $"Move {move.MoveIndex} does not exist.";
                    }

                    if (request.DisabledMoves.TryGetValue(move.Slot, out var disabled) && disabled.Contains(move.MoveIndex))
                    {
                        return $"Can't move: {mover.Set.Species}'s {moves[move.MoveIndex].Name} is disabled.";
                    }

                    if (move.TargetSlot < 0 || move.TargetSlot >= side.Active.Count)
                    {
                        return $"Target slot {move.TargetSlot} does not exist.";
                    }
                    break;

                case SwitchChoice sw when request.Kind == DecisionKind.Turn && request.TrappedSlots.Contains(sw.Slot):
                    return "Can't switch: the active Pokémon is trapped.";

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
