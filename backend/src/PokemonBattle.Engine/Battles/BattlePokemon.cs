using PokemonBattle.Engine.Pokemon;
using PokemonBattle.Engine.Moves;

namespace PokemonBattle.Engine.Battles;

/// <summary>Mutable in-battle state of a team member.</summary>
public sealed class BattlePokemon
{
    private readonly int[] _stages = new int[Enum.GetValues<BoostStat>().Length];

    private readonly BattleField _field;

    internal BattlePokemon(SideId side, int teamIndex, PokemonSet set, BattleField field)
    {
        Side = side;
        TeamIndex = teamIndex;
        Set = set;
        CurrentHp = set.Stats.Hp;
        Item = set.Item;
        _field = field;
        RestoreAbility();
    }

    /// <summary>Current ability: the set's, unless Trace replaced it while on the field.</summary>
    public string? Ability { get; private set; }

    internal bool AbilityBreakable { get; private set; }
    internal bool AbilityTraceable { get; private set; }

    /// <summary>Trace keeps looking for an ability to copy while no foe has a copyable one.</summary>
    internal bool TraceSeeking { get; set; }

    internal void ChangeAbility(string? ability, bool breakable, bool traceable)
    {
        Ability = ability;
        AbilityBreakable = breakable;
        AbilityTraceable = traceable;
    }

    private void RestoreAbility() => ChangeAbility(Set.Ability, Set.AbilityBreakable, Set.AbilityTraceable);

    /// <summary>
    /// Held item right now: the set's until it is used up (Normal Gem, Focus Sash...) or passed on
    /// (Sticky Barb). A consumed item stays gone after switching out, as in Showdown.
    /// </summary>
    public string? Item { get; private set; }

    /// <summary>
    /// The last item it used up or ate (Showdown's lastItem), which Harvest (and Recycle) can give
    /// back. Items lost without being used (a popped Air Balloon, a Sticky Barb passed on) are not
    /// recorded. It stays when the Pokémon switches out (Gen 5+).
    /// </summary>
    internal string? LastItem { get; private set; }

    /// <summary>The held item is used up or eaten (Showdown's useItem / eatItem): it becomes the last item.</summary>
    internal void ConsumeItem()
    {
        LastItem = Item;
        Item = null;
    }

    /// <summary>The held item is gone without being used (a popped Air Balloon, a Sticky Barb passed on).</summary>
    internal void LoseItem() => Item = null;

    /// <summary>Receives an item (a Sticky Barb passed on by contact).</summary>
    internal void SetItem(string item) => Item = item;

    /// <summary>Holds its last used item again (Harvest's setItem(lastItem); lastItem is cleared).</summary>
    internal void RestoreLastItem()
    {
        Item = LastItem;
        LastItem = null;
    }

    /// <summary>The Focus Energy effect (critical hit ratio +2) until it leaves the field (Lansat Berry).</summary>
    internal bool FocusEnergy { get; set; }

    /// <summary>
    /// Turns left of the eaten Micle Berry's boost to its next move's accuracy (Showdown's micleberry
    /// volatile, duration 2), 0 when none.
    /// </summary>
    internal int MicleTurns { get; set; }

    /// <summary>Whether any stat stage is below 0 (White Herb's trigger).</summary>
    internal bool HasNegativeStages => _stages.Any(stage => stage < 0);

    /// <summary>Resets every lowered stage to 0 and keeps the raised ones (White Herb).</summary>
    internal void ClearNegativeStages()
    {
        for (var i = 0; i < _stages.Length; i++)
        {
            _stages[i] = Math.Max(0, _stages[i]);
        }
    }

    public SideId Side { get; }
    public int TeamIndex { get; }
    public PokemonSet Set { get; }
    public int CurrentHp { get; private set; }
    public bool IsFainted => CurrentHp == 0;

    /// <summary>Major status, if any. It stays when the Pokémon switches out.</summary>
    public MajorStatus? Status { get; private set; }

    /// <summary>Remaining sleep counter; the Pokémon wakes when a move attempt brings it to 0.</summary>
    internal int SleepTurns { get; set; }

    /// <summary>Bad poison counter: damage is n/16 of max HP. Restarts when switching in.</summary>
    internal int ToxicStage { get; set; }

    /// <summary>
    /// Speed used for turn order, as Showdown's getStat: stat stage, then the chained ModifySpe
    /// abilities (Chlorophyll, Swift Swim, Sand Rush x2 in their weather; Quick Feet x1.5 when
    /// statused) and held item (Choice Scarf, Quick Powder, Iron Ball...; items run after abilities),
    /// then paralysis last (Gen 6: x0.25, floored; Quick Feet ignores it).
    /// </summary>
    public int Speed
    {
        get
        {
            var speed = StatStages.ApplyToStat(Set.Stats.Speed, GetStage(BoostStat.Speed));
            var weather = _field.EffectiveWeather;
            var chain = new ModifierChain();
            switch (Ability)
            {
                case "chlorophyll" when weather == Weather.Sun:
                case "swiftswim" when weather == Weather.Rain:
                case "sandrush" when weather == Weather.Sandstorm:
                    chain = chain.Chain(2);
                    break;
                case "quickfeet" when Status is not null:
                    chain = chain.Chain(3, 2);
                    break;
            }

            speed = HeldItems.ChainSpeed(chain, Item, Set.Species).Apply(speed);
            return Status == MajorStatus.Paralysis && Ability != "quickfeet" ? speed * 25 / 100 : speed;
        }
    }

    /// <summary>Weight used by weight-based moves, in hectograms (at least 1).</summary>
    public int WeightHg => Math.Max(1, Set.WeightHg);

    /// <summary>Sum of the positive stat stages (Stored Power, Punishment).</summary>
    public int PositiveStages => _stages.Where(stage => stage > 0).Sum();

    /// <summary>Damage taken from moves this turn, oldest first (Counter, Revenge, Metal Burst...).</summary>
    internal List<HitTaken> HitsTakenThisTurn { get; } = [];

    /// <summary>Took damage of any kind this turn (Assurance).</summary>
    internal bool HurtThisTurn { get; set; }

    /// <summary>Entered the field this turn (Payback is not boosted against it).</summary>
    internal bool NewlySwitched { get; set; }

    /// <summary>Two-turn move being charged (Solar Beam, Fly...), or null.</summary>
    internal string? ChargingMoveId { get; private set; }

    /// <summary>Choice repeated automatically on the turn after charging.</summary>
    internal MoveChoice? ChargedChoice { get; private set; }

    /// <summary>Must spend its next turn recharging (Hyper Beam...).</summary>
    internal bool MustRecharge { get; set; }

    /// <summary>Flash Fire absorbed a Fire move: its own Fire moves are stronger until it leaves the field.</summary>
    internal bool FlashFireActive { get; set; }

    /// <summary>Last move the Metronome item saw, and how many consecutive uses it counts (its condition's state).</summary>
    internal string? MetronomeMove { get; set; }

    internal int MetronomeCount { get; set; }

    /// <summary>Its move succeeded this turn / last turn (Showdown's moveThisTurnResult / moveLastTurnResult).</summary>
    internal bool MoveSucceededThisTurn { get; set; }

    internal bool MoveSucceededLastTurn { get; private set; }

    /// <summary>
    /// Move a Choice item locked it into (Showdown's choicelock volatile), or null. Unlike
    /// <see cref="IsLocked"/>, its player still chooses, among the moves the lock allows.
    /// </summary>
    internal string? ChoiceLockedMoveId { get; set; }

    /// <summary>Its next action is fixed, so its player is not asked for a decision.</summary>
    internal bool IsLocked => ChargedChoice is not null || MustRecharge;

    internal void StartCharging(string moveId, MoveChoice choice)
    {
        ChargingMoveId = moveId;
        ChargedChoice = choice;
    }

    internal void StopCharging()
    {
        ChargingMoveId = null;
        ChargedChoice = null;
    }

    /// <summary>Volatile state is lost when the Pokémon leaves the field.</summary>
    internal void LeaveField()
    {
        Array.Clear(_stages);
        StopCharging();
        MustRecharge = false;
        ChoiceLockedMoveId = null;
        FlashFireActive = false;
        TraceSeeking = false;
        MetronomeMove = null;
        MetronomeCount = 0;
        FocusEnergy = false;
        MicleTurns = 0;
        MoveSucceededThisTurn = false;
        MoveSucceededLastTurn = false;
        RestoreAbility();
    }

    internal void ResetTurnState()
    {
        HitsTakenThisTurn.Clear();
        HurtThisTurn = false;
        NewlySwitched = false;
        MoveSucceededLastTurn = MoveSucceededThisTurn;
        MoveSucceededThisTurn = false;
    }

    internal int Heal(int amount)
    {
        var healed = Math.Min(amount, Set.Stats.Hp - CurrentHp);
        CurrentHp += healed;
        return healed;
    }

    internal void SetStatus(MajorStatus status)
    {
        Status = status;
        ToxicStage = 0;
    }

    internal void CureStatus() => Status = null;

    /// <summary>Current stage of <paramref name="stat"/>, from -6 to +6.</summary>
    public int GetStage(BoostStat stat) => _stages[(int)stat];

    internal int TakeDamage(int amount)
    {
        var dealt = Math.Min(amount, CurrentHp);
        CurrentHp -= dealt;
        return dealt;
    }

    /// <summary>Changes a stage within [-6, 6]. Returns the change actually applied (0 at the limit).</summary>
    internal int ChangeStage(BoostStat stat, int stages)
    {
        var current = _stages[(int)stat];
        var next = Math.Clamp(current + stages, StatStages.Min, StatStages.Max);
        _stages[(int)stat] = next;
        return next - current;
    }
}

/// <param name="Slot">Slot the attacker occupied when it hit (Counter aims back at it).</param>
internal sealed record HitTaken(BattlePokemon Source, SideId Side, int Slot, int Damage, MoveCategory Category);
