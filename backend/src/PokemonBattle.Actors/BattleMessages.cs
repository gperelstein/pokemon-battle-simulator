using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Actors;

/// <summary>Sends in the leads and opens the first turn.</summary>
public sealed record StartBattle;

/// <summary>A player's answer to its pending <see cref="DecisionRequest"/>.</summary>
public sealed record SubmitDecision(SideId Side, Decision Decision);

/// <summary>Reply to the sender: the decision was stored or resolved.</summary>
public sealed record DecisionAccepted(SideId Side);

/// <summary>Reply to the sender: the decision is invalid or not expected right now.</summary>
public sealed record DecisionRejected(SideId Side, string Reason);

/// <summary>
/// Pushed to every player whenever the battle advances. <paramref name="Request"/> is the
/// decision this player must make now, or null when it has to wait for the opponent.
/// </summary>
public sealed record BattleUpdate(IReadOnlyList<BattleEvent> Events, DecisionRequest? Request);
