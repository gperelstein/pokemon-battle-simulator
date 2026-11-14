namespace PokemonBattle.Engine.Battles;

public enum DecisionKind
{
    /// <summary>Start of turn: choose a move or a switch for every live active slot.</summary>
    Turn,

    /// <summary>
    /// Mid-turn: a self-switch move (U-turn, Volt Switch...) requires a replacement right now.
    /// The rest of the turn stays queued until this decision arrives.
    /// </summary>
    MidTurnSwitch,

    /// <summary>End of turn: replace fainted Pokémon.</summary>
    FaintReplacement,
}

/// <summary>What the engine is waiting for from one player.</summary>
/// <param name="TrappedSlots">
/// Requested slots whose Pokémon cannot choose to switch (Arena Trap, Magnet Pull, Shadow Tag).
/// </param>
public sealed record DecisionRequest(
    SideId Side, DecisionKind Kind, IReadOnlyList<int> Slots, IReadOnlyList<int>? TrappedSlots = null)
{
    public IReadOnlyList<int> TrappedSlots { get; init; } = TrappedSlots ?? [];
}

public abstract record SlotChoice(int Slot);

/// <param name="TargetSlot">Opposing slot to target. Ignored for spread moves; defaults to 0.</param>
public sealed record MoveChoice(int Slot, int MoveIndex, int TargetSlot = 0) : SlotChoice(Slot);

public sealed record SwitchChoice(int Slot, int TeamIndex) : SlotChoice(Slot);

/// <summary>A player's answer to a <see cref="DecisionRequest"/>: one choice per requested slot.</summary>
public sealed record Decision(IReadOnlyList<SlotChoice> Choices)
{
    public Decision(params SlotChoice[] choices) : this((IReadOnlyList<SlotChoice>)choices) { }
}

public sealed record SubmitResult(bool Accepted, string? Error, IReadOnlyList<BattleEvent> Events)
{
    internal static SubmitResult Ok(IReadOnlyList<BattleEvent> events) => new(true, null, events);
    internal static SubmitResult Reject(string error) => new(false, error, []);
}
