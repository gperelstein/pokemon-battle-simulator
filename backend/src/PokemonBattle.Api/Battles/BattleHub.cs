using Microsoft.AspNetCore.SignalR;
using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Api.Battles;

/// <summary>An event on the wire: its type name and its fields.</summary>
public sealed record EventDto(string Type, object Data)
{
    public static EventDto From(BattleEvent e) => new(e.GetType().Name, e);
}

/// <summary>What <see cref="BattleHub.Join"/> and <see cref="BattleHub.View"/> return.</summary>
public sealed record JoinedBattle(Guid BattleId, SideId Side, BattleFormat Format, IReadOnlyList<EventDto> Events, DecisionRequest? Request);

/// <summary>Pushed to a player as "Update"; see <see cref="PlayerUpdate"/>.</summary>
public sealed record UpdateDto(int FirstEventIndex, IReadOnlyList<EventDto> Events, DecisionRequest? Request);

/// <summary>
/// One choice: <c>kind</c> is "move" (<c>moveIndex</c>, <c>targetSlot</c>, <c>megaEvolve</c>) or
/// "switch" (<c>teamIndex</c>). There is no side: it comes from the connection.
/// </summary>
public sealed record ChoiceDto(int Slot, string? Kind, int MoveIndex = 0, int TargetSlot = 0, bool MegaEvolve = false, int TeamIndex = 0);

public sealed record DecisionDto(IReadOnlyList<ChoiceDto>? Choices);

/// <summary>
/// Real-time battle channel (<c>/hubs/battle</c>). A connection joins one battle with its ticket's
/// token and stays bound to that side (L5): decisions never name a side. Each player receives
/// "Update" messages with its own events and request, so mid-turn requests reach only the player
/// that must answer (S5). A client can (re)join at any time and gets its whole history; an update
/// whose <c>firstEventIndex</c> is below the number of events it already has repeats them.
/// </summary>
public sealed class BattleHub(BattleRegistry registry) : Hub
{
    private const string BindingKey = "battle";

    internal static string GroupOf(Guid battleId, SideId side) => $"{battleId}:{side}";

    public async Task<JoinedBattle> Join(Guid battleId, string token)
    {
        if (Context.Items.ContainsKey(BindingKey))
        {
            throw new HubException("This connection already joined a battle.");
        }

        if (registry.Find(battleId) is not { } session || session.Authenticate(token) is not { } side)
        {
            throw new HubException("Unknown battle or invalid token.");
        }

        // Subscribe before reading the history, so no update falls in between (it may repeat).
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(battleId, side));
        Context.Items[BindingKey] = (session, side);
        return ViewOf(session, side);
    }

    public JoinedBattle View()
    {
        var (session, side) = Binding();
        return ViewOf(session, side);
    }

    public async Task<SubmitOutcome> Submit(DecisionDto decision)
    {
        var (session, side) = Binding();
        var choices = new List<SlotChoice>();
        foreach (var choice in decision.Choices ?? [])
        {
            switch (choice.Kind?.ToLowerInvariant())
            {
                case "move":
                    choices.Add(new MoveChoice(choice.Slot, choice.MoveIndex, choice.TargetSlot, choice.MegaEvolve));
                    break;
                case "switch":
                    choices.Add(new SwitchChoice(choice.Slot, choice.TeamIndex));
                    break;
                default:
                    return new SubmitOutcome(false, $"Unknown choice kind '{choice.Kind}'.", session.ViewOf(side).Request);
            }
        }

        return await session.SubmitAsync(side, new Decision(choices));
    }

    private (BattleSession Session, SideId Side) Binding() =>
        Context.Items.TryGetValue(BindingKey, out var binding)
            ? ((BattleSession, SideId))binding!
            : throw new HubException("Join a battle first.");

    private static JoinedBattle ViewOf(BattleSession session, SideId side)
    {
        var view = session.ViewOf(side);
        return new JoinedBattle(session.Id, side, session.Format, view.Events.Select(EventDto.From).ToArray(), view.Request);
    }
}

/// <summary>Sends each player's updates to the connections that joined with its token.</summary>
internal sealed class HubPlayerNotifier(IHubContext<BattleHub> hub) : IPlayerNotifier
{
    public Task NotifyAsync(Guid battleId, SideId side, PlayerUpdate update) =>
        hub.Clients.Group(BattleHub.GroupOf(battleId, side)).SendAsync(
            "Update", new UpdateDto(update.FirstEventIndex, update.Events.Select(EventDto.From).ToArray(), update.Request));
}
