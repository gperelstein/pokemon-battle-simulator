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
        _item = set.ItemData ?? (set.Item is null ? null : new ItemDefinition(set.Item, set.PlateType));
        _field = field;
        CanMegaEvolve = HoldsOwnMegaStone(set);
        RestoreAbility();
    }

    /// <summary>
    /// It can still Mega Evolve (Showdown's canMegaEvo): decided once from the set's held Mega Stone,
    /// which cannot leave its own species; cleared for the whole side once one of them Mega Evolves.
    /// </summary>
    internal bool CanMegaEvolve { get; set; }

    /// <summary>
    /// canMegaEvo: the held item is the forme's stone, bound to the set's base species (the stone's
    /// megaStone key), and the set is not that forme already.
    /// </summary>
    private static bool HoldsOwnMegaStone(PokemonSet set)
    {
        if (set.Mega is not { } mega || set.Item != mega.Item || set.Species == mega.Species)
        {
            return false;
        }

        var bound = set.ItemData?.BoundSpecies ?? [];
        return bound.Count == 0 || bound.Contains(set.BaseSpecies ?? set.Species);
    }

    /// <summary>
    /// Takes its Mega forme for the rest of the battle (Showdown's formeChange with isPermanent):
    /// species, types, stats (HP unchanged), weight and ability change at once; the ability becomes its
    /// base ability, so it is the one restored on switching out. Returns the species it had before.
    /// </summary>
    internal string MegaEvolve()
    {
        var mega = Set.Mega!;
        var before = Set.Species;
        Set = Set with
        {
            Species = mega.Species,
            BaseSpecies = Set.BaseSpecies ?? Set.Species,
            Types = mega.Types,
            Stats = mega.Stats with { Hp = Set.Stats.Hp },
            WeightHg = mega.WeightHg,
            Ability = mega.Ability,
            AbilityBreakable = mega.AbilityBreakable,
            AbilityTraceable = mega.AbilityTraceable,
            CanEvolve = false,
        };
        TraceSeeking = false;
        RestoreAbility();
        return before;
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

    private ItemDefinition? _item;
    private ItemDefinition? _lastItem;

    /// <summary>
    /// Held item right now: the set's until it is used up (Normal Gem, Focus Sash...), taken (Knock
    /// Off, Thief) or passed on (Sticky Barb, Trick). A lost item stays gone after switching out, as
    /// in Showdown.
    /// </summary>
    public string? Item => _item?.Id;

    /// <summary>The held item with its battle data (Plate type, Fling, Natural Gift...), or null.</summary>
    internal ItemDefinition? HeldItem => _item;

    /// <summary>
    /// The last item it used up or ate (Showdown's lastItem), which Harvest and Recycle can give back
    /// and Pickup can take. Items lost without being used (a popped Air Balloon, a Sticky Barb passed
    /// on, a knocked-off or stolen item) are not recorded. It stays when the Pokémon switches out (Gen 5+).
    /// </summary>
    internal string? LastItem => _lastItem?.Id;

    /// <summary>Used up or ate an item this turn (Showdown's usedItemThisTurn, which Pickup reads).</summary>
    internal bool UsedItemThisTurn { get; private set; }

    /// <summary>
    /// The unburden volatile: added when an Unburden holder loses its item (onAfterUseItem,
    /// onTakeItem); it doubles Speed while the holder has no item, until it leaves the field.
    /// </summary>
    internal bool UnburdenActive { get; private set; }

    /// <summary>
    /// The held item is used up or eaten (Showdown's useItem / eatItem, Fling, Natural Gift): it
    /// becomes the last item.
    /// </summary>
    internal void ConsumeItem()
    {
        _lastItem = _item;
        UsedItemThisTurn = true;
        DropItem();
    }

    /// <summary>
    /// The held item is gone without being used (a popped Air Balloon, a Sticky Barb passed on, Knock
    /// Off, Thief, Trick: Showdown's takeItem). Returns it.
    /// </summary>
    internal ItemDefinition? LoseItem()
    {
        var item = _item;
        DropItem();
        return item;
    }

    private void DropItem()
    {
        _item = null;
        if (Ability == "unburden")
        {
            UnburdenActive = true;
        }
    }

    /// <summary>Receives an item (Sticky Barb, Thief, Trick, Pickup: Showdown's setItem).</summary>
    internal void SetItem(ItemDefinition item) => _item = item;

    /// <summary>Holds its last used item again (Harvest's and Recycle's setItem(lastItem); lastItem is cleared).</summary>
    internal void RestoreLastItem()
    {
        _item = _lastItem;
        _lastItem = null;
    }

    /// <summary>Gives its last item away (Pickup clears the lastItem it takes).</summary>
    internal ItemDefinition? TakeLastItem()
    {
        var item = _lastItem;
        _lastItem = null;
        return item;
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
    /// <summary>The set as it is now: the Mega forme's once it Mega Evolved.</summary>
    public PokemonSet Set { get; private set; }
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
                case "unburden" when UnburdenActive && _item is null:
                    chain = chain.Chain(2);
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
        UsedItemThisTurn = false;
        UnburdenActive = false;
        RestoreAbility();
    }

    /// <param name="itemUse">
    /// Also forget the items used this turn; false on turn 1, when items used by the leads on
    /// switch-in still count (sim/battle.ts skips the reset while turn is 1).
    /// </param>
    internal void ResetTurnState(bool itemUse = true)
    {
        if (itemUse)
        {
            UsedItemThisTurn = false;
        }

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
