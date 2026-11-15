using PokemonBattle.Catalog;
using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Api.Battles;

/// <summary>Body of <c>POST /battles</c>: the format and the creator's team.</summary>
public sealed record CreateBattleBody(BattleFormat Format, IReadOnlyList<PokemonSetSpec>? Team);

/// <summary>Body of <c>POST /battles/{id}/join</c>: the second player's team.</summary>
public sealed record JoinBattleBody(IReadOnlyList<PokemonSetSpec>? Team);

/// <summary>
/// What an anonymous player keeps to play a battle: the token binds its connection to
/// <paramref name="Side"/> (S6, L5). It is only returned once, to the player that created or joined.
/// </summary>
public sealed record PlayerTicket(Guid BattleId, SideId Side, string Token);

/// <summary>Public state of a battle (<c>GET /battles/{id}</c>): no tokens and no teams.</summary>
public sealed record BattleSummary(Guid BattleId, BattleFormat Format, string Status);

/// <summary>What one player sees: every event so far and its own pending request, if any.</summary>
public sealed record PlayerView(IReadOnlyList<BattleEvent> Events, DecisionRequest? Request);

/// <summary>
/// The result of a decision. A rejection carries the player's pending request, which may have
/// changed (a switch from a trapped slot reveals the trap).
/// </summary>
public sealed record SubmitOutcome(bool Accepted, string? Error, DecisionRequest? Request);
