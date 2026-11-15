using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PokemonBattle.Api.Battles;
using PokemonBattle.Engine.Battles;
using static PokemonBattle.Api.Tests.LobbyTests;

namespace PokemonBattle.Api.Tests;

/// <summary>
/// SignalR hub: a connection joins with its ticket's token and is bound to that side (L5, S6);
/// each player receives the events plus only its own request, also mid-turn (S5).
/// </summary>
public class BattleHubTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static readonly object[] DugtrioTeam =
    [
        new { species = "Dugtrio", moves = new[] { "Earthquake" }, ability = "Arena Trap" },
    ];

    [Fact]
    public async Task Joining_with_a_ticket_returns_the_players_view()
    {
        var (p1Ticket, _) = await Open(PikachuTeam, SnorlaxTeam);
        await using var p1 = await Connect();

        var view = await p1.Join(p1Ticket);

        Assert.Equal(SideId.P1, view.GetProperty("side").Deserialize<SideId>(ApiJson.Options));
        Assert.Contains(Types(view), type => type == "SwitchedIn");
        Assert.Equal("Turn", view.GetProperty("request").GetProperty("kind").GetString());
        Assert.Equal("P1", view.GetProperty("request").GetProperty("side").GetString());
    }

    [Fact]
    public async Task A_wrong_token_cannot_join()
    {
        var (p1Ticket, _) = await Open(PikachuTeam, SnorlaxTeam);
        await using var intruder = await Connect();

        var error = await Assert.ThrowsAsync<HubException>(() => intruder.Join(p1Ticket with { Token = "guess" }));

        Assert.Contains("Unknown battle or invalid token.", error.Message);
    }

    [Fact]
    public async Task A_connection_must_join_before_deciding_and_joins_only_once()
    {
        var (p1Ticket, _) = await Open(PikachuTeam, SnorlaxTeam);
        await using var p1 = await Connect();

        var early = await Assert.ThrowsAsync<HubException>(() => p1.Submit(Move(0)));
        await p1.Join(p1Ticket);
        var twice = await Assert.ThrowsAsync<HubException>(() => p1.Join(p1Ticket));

        Assert.Contains("Join a battle first.", early.Message);
        Assert.Contains("This connection already joined a battle.", twice.Message);
    }

    [Fact]
    public async Task A_player_never_sees_the_opponents_requests()
    {
        var (p1Ticket, p2Ticket) = await Open(PikachuTeam, SnorlaxTeam);
        await using var p1 = await Connect();
        await using var p2 = await Connect();

        var p1View = await p1.Join(p1Ticket);
        var p2View = await p2.Join(p2Ticket);

        Assert.All(Requested(p1View), side => Assert.Equal("P1", side));
        Assert.All(Requested(p2View), side => Assert.Equal("P2", side));
        Assert.Equal("P2", p2View.GetProperty("request").GetProperty("side").GetString());
    }

    [Fact]
    public async Task A_turn_reaches_both_players_with_their_next_requests()
    {
        // Protect keeps both Pokémon standing, so both players get a turn request (no faint replacement).
        var protector = new object[] { new { species = "Snorlax", moves = new[] { "Protect" }, ability = "Thick Fat" } };
        var (p1Ticket, p2Ticket) = await Open(PikachuTeam, protector);
        await using var p1 = await Connect();
        await using var p2 = await Connect();
        await p1.Join(p1Ticket);
        await p2.Join(p2Ticket);

        Assert.True((await p1.Submit(Move(0))).GetProperty("accepted").GetBoolean());
        Assert.True((await p2.Submit(Move(0))).GetProperty("accepted").GetBoolean());

        var p1Update = await p1.NextUpdate();
        var p2Update = await p2.NextUpdate();
        Assert.Contains("MoveUsed", Types(p1Update));
        Assert.Equal(Types(p1Update).Where(t => t != "DecisionRequested"), Types(p2Update).Where(t => t != "DecisionRequested"));
        Assert.All(Requested(p2Update), side => Assert.Equal("P2", side));
        Assert.Equal("P1", p1Update.GetProperty("request").GetProperty("side").GetString());
        Assert.Equal("P2", p2Update.GetProperty("request").GetProperty("side").GetString());
        Assert.Equal(p1Update.GetProperty("firstEventIndex").GetInt32(), (await p1.View()).GetProperty("events").GetArrayLength() - p1Update.GetProperty("events").GetArrayLength());
    }

    [Fact]
    public async Task A_mid_turn_switch_is_asked_only_to_the_volt_switch_user()
    {
        var (p1Ticket, p2Ticket) = await Open(PikachuTeam, SnorlaxTeam);
        await using var p1 = await Connect();
        await using var p2 = await Connect();
        await p1.Join(p1Ticket);
        await p2.Join(p2Ticket);

        await p1.Submit(Move(1)); // Volt Switch
        await p2.Submit(Move(0));

        var p1Update = await p1.NextUpdate();
        var p2Update = await p2.NextUpdate();
        Assert.Equal("MidTurnSwitch", p1Update.GetProperty("request").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, p2Update.GetProperty("request").ValueKind);

        var p2Early = await p2.Submit(Move(0));
        Assert.False(p2Early.GetProperty("accepted").GetBoolean());
        Assert.Equal("P2 has no pending decision.", p2Early.GetProperty("error").GetString());

        Assert.True((await p1.Submit(Switch(0, 1))).GetProperty("accepted").GetBoolean());
        var resumed = await p1.NextUpdate();
        Assert.Contains(resumed.GetProperty("events").EnumerateArray(), e =>
            e.GetProperty("type").GetString() == "SwitchedIn" && e.GetProperty("data").GetProperty("species").GetString() == "Jolteon");
        Assert.Equal("Turn", resumed.GetProperty("request").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_rejected_switch_from_a_trapped_slot_returns_and_pushes_the_revealed_request()
    {
        var (p1Ticket, _) = await Open(PikachuTeam, DugtrioTeam);
        await using var p1 = await Connect();
        var view = await p1.Join(p1Ticket);
        Assert.Equal([0], Slots(view.GetProperty("request"), "maybeTrappedSlots"));

        var outcome = await p1.Submit(Switch(0, 1));

        Assert.False(outcome.GetProperty("accepted").GetBoolean());
        Assert.Equal("Can't switch: the active Pokémon is trapped.", outcome.GetProperty("error").GetString());
        Assert.Equal([0], Slots(outcome.GetProperty("request"), "trappedSlots"));
        var pushed = await p1.NextUpdate();
        Assert.Equal(0, pushed.GetProperty("events").GetArrayLength());
        Assert.Equal([0], Slots(pushed.GetProperty("request"), "trappedSlots"));
    }

    [Fact]
    public async Task An_unknown_choice_kind_is_rejected()
    {
        var (p1Ticket, _) = await Open(PikachuTeam, SnorlaxTeam);
        await using var p1 = await Connect();
        await p1.Join(p1Ticket);

        var outcome = await p1.Submit(new { choices = new[] { new { slot = 0, kind = "run" } } });

        Assert.False(outcome.GetProperty("accepted").GetBoolean());
        Assert.Equal("Unknown choice kind 'run'.", outcome.GetProperty("error").GetString());
    }

    private static object Move(int moveIndex) => new { choices = new[] { new { slot = 0, kind = "move", moveIndex } } };

    private static object Switch(int slot, int teamIndex) => new { choices = new[] { new { slot, kind = "switch", teamIndex } } };

    private static IEnumerable<string> Types(JsonElement viewOrUpdate) =>
        viewOrUpdate.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()!);

    private static IEnumerable<string> Requested(JsonElement view) =>
        view.GetProperty("events").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "DecisionRequested")
            .Select(e => e.GetProperty("data").GetProperty("request").GetProperty("side").GetString()!);

    private static int[] Slots(JsonElement request, string name) =>
        request.GetProperty(name).EnumerateArray().Select(e => e.GetInt32()).ToArray();

    private async Task<(PlayerTicket P1, PlayerTicket P2)> Open(object[] p1Team, object[] p2Team)
    {
        var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/battles", new { format = "singles", team = p1Team });
        var p1 = (await created.Content.ReadFromJsonAsync<PlayerTicket>(ApiJson.Options))!;
        var joined = await client.PostAsJsonAsync($"/battles/{p1.BattleId}/join", new { team = p2Team });
        var p2 = (await joined.Content.ReadFromJsonAsync<PlayerTicket>(ApiJson.Options))!;
        var session = factory.Services.GetService(typeof(BattleRegistry)) as BattleRegistry;
        await Eventually(() => session!.Find(p1.BattleId)!.ViewOf(SideId.P2).Request);
        await Eventually(() => session!.Find(p1.BattleId)!.ViewOf(SideId.P1).Request);
        return (p1, p2);
    }

    private async Task<Player> Connect()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/battle"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .AddJsonProtocol(options => ApiJson.Configure(options.PayloadSerializerOptions))
            .Build();
        var player = new Player(connection);
        await connection.StartAsync();
        return player;
    }

    private sealed class Player : IAsyncDisposable
    {
        private readonly HubConnection _connection;
        private readonly Channel<JsonElement> _updates = Channel.CreateUnbounded<JsonElement>();

        public Player(HubConnection connection)
        {
            _connection = connection;
            _connection.On<JsonElement>("Update", update => _updates.Writer.TryWrite(update));
        }

        public Task<JsonElement> Join(PlayerTicket ticket) =>
            _connection.InvokeAsync<JsonElement>("Join", ticket.BattleId, ticket.Token);

        public Task<JsonElement> View() => _connection.InvokeAsync<JsonElement>("View");

        public Task<JsonElement> Submit(object decision) => _connection.InvokeAsync<JsonElement>("Submit", decision);

        public async Task<JsonElement> NextUpdate()
        {
            using var timeout = new CancellationTokenSource(Wait);
            return await _updates.Reader.ReadAsync(timeout.Token);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
