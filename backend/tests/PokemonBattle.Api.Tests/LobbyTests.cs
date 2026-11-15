using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PokemonBattle.Api.Battles;
using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Api.Tests;

/// <summary>HTTP lobby: anonymous players create and join battles and get a per-side token (S6).</summary>
public class LobbyTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    internal static readonly object[] PikachuTeam =
    [
        new { species = "Pikachu", moves = new[] { "Thunderbolt", "Volt Switch" }, ability = "Static" },
        new { species = "Jolteon", moves = new[] { "Thunderbolt" }, ability = "Volt Absorb" },
    ];

    internal static readonly object[] SnorlaxTeam =
    [
        new { species = "Snorlax", moves = new[] { "Body Slam" }, ability = "Thick Fat" },
    ];

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Creating_a_battle_returns_the_first_players_ticket()
    {
        var response = await _client.PostAsJsonAsync("/battles", new { format = "singles", team = PikachuTeam });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = await Ticket(response);
        Assert.Equal(SideId.P1, ticket.Side);
        Assert.False(string.IsNullOrWhiteSpace(ticket.Token));
        Assert.Equal($"/battles/{ticket.BattleId}", response.Headers.Location!.OriginalString);
        Assert.Equal("waiting", await Status(ticket.BattleId));
    }

    [Fact]
    public async Task An_illegal_team_is_rejected_with_the_team_builder_errors()
    {
        var team = new[] { new { species = "Pikachu", moves = new[] { "Thunderbolt" }, ability = "Drought" } };

        var response = await _client.PostAsJsonAsync("/battles", new { format = "singles", team });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Pikachu cannot have the ability 'Drought'", body);
    }

    [Fact]
    public async Task Joining_returns_the_second_players_ticket_and_starts_the_battle()
    {
        var first = await Create();

        var response = await _client.PostAsJsonAsync($"/battles/{first.BattleId}/join", new { team = SnorlaxTeam });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var second = await Ticket(response);
        Assert.Equal(first.BattleId, second.BattleId);
        Assert.Equal(SideId.P2, second.Side);
        Assert.NotEqual(first.Token, second.Token);
        Assert.Equal("active", await Status(first.BattleId));
    }

    [Fact]
    public async Task A_battle_accepts_only_two_players()
    {
        var first = await Create();
        await _client.PostAsJsonAsync($"/battles/{first.BattleId}/join", new { team = SnorlaxTeam });

        var third = await _client.PostAsJsonAsync($"/battles/{first.BattleId}/join", new { team = SnorlaxTeam });

        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
    }

    [Fact]
    public async Task An_unknown_battle_cannot_be_joined_or_read()
    {
        var id = Guid.NewGuid();

        var join = await _client.PostAsJsonAsync($"/battles/{id}/join", new { team = SnorlaxTeam });
        var read = await _client.GetAsync($"/battles/{id}");

        Assert.Equal(HttpStatusCode.NotFound, join.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task A_doubles_battle_needs_two_pokemon_per_team()
    {
        var response = await _client.PostAsJsonAsync("/battles", new { format = "doubles", team = SnorlaxTeam });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("at least 2", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_status_never_exposes_tokens_or_teams()
    {
        var first = await Create();

        var body = await _client.GetStringAsync($"/battles/{first.BattleId}");

        Assert.DoesNotContain(first.Token, body);
        Assert.DoesNotContain("Pikachu", body);
    }

    [Fact]
    public async Task Each_token_authenticates_only_its_own_side()
    {
        var first = await Create();
        var second = await Ticket(await _client.PostAsJsonAsync($"/battles/{first.BattleId}/join", new { team = SnorlaxTeam }));
        var session = factory.Services.GetRequiredService<BattleRegistry>().Find(first.BattleId)!;

        Assert.Equal(SideId.P1, session.Authenticate(first.Token));
        Assert.Equal(SideId.P2, session.Authenticate(second.Token));
        Assert.Null(session.Authenticate("not-a-token"));
        Assert.Null(session.Authenticate(""));
    }

    [Fact]
    public async Task Both_players_get_their_first_turn_request_once_the_battle_starts()
    {
        var first = await Create();
        await _client.PostAsJsonAsync($"/battles/{first.BattleId}/join", new { team = SnorlaxTeam });
        var session = factory.Services.GetRequiredService<BattleRegistry>().Find(first.BattleId)!;

        var p1 = await Eventually(() => session.ViewOf(SideId.P1).Request);
        var p2 = await Eventually(() => session.ViewOf(SideId.P2).Request);

        Assert.Equal(DecisionKind.Turn, p1.Kind);
        Assert.Equal(SideId.P1, p1.Side);
        Assert.Equal(SideId.P2, p2.Side);
        Assert.Contains(session.ViewOf(SideId.P1).Events, e => e is SwitchedIn { Species: "Pikachu" });
    }

    [Fact]
    public async Task Decisions_go_through_the_session_with_the_bound_side()
    {
        var first = await Create();
        await _client.PostAsJsonAsync($"/battles/{first.BattleId}/join", new { team = SnorlaxTeam });
        var session = factory.Services.GetRequiredService<BattleRegistry>().Find(first.BattleId)!;
        await Eventually(() => session.ViewOf(SideId.P1).Request);

        var invalid = await session.SubmitAsync(SideId.P1, new Decision(new MoveChoice(0, 3)));
        var valid = await session.SubmitAsync(SideId.P1, new Decision(new MoveChoice(0, 0)));

        Assert.False(invalid.Accepted);
        Assert.Equal("Move 3 does not exist.", invalid.Error);
        Assert.Equal(DecisionKind.Turn, invalid.Request!.Kind);
        Assert.True(valid.Accepted);
    }

    private async Task<PlayerTicket> Create()
    {
        var response = await _client.PostAsJsonAsync("/battles", new { format = "singles", team = PikachuTeam });
        response.EnsureSuccessStatusCode();
        return await Ticket(response);
    }

    private async Task<string> Status(Guid battleId)
    {
        using var json = JsonDocument.Parse(await _client.GetStringAsync($"/battles/{battleId}"));
        return json.RootElement.GetProperty("status").GetString()!;
    }

    private static async Task<PlayerTicket> Ticket(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<PlayerTicket>(ApiJson.Options))!;

    internal static async Task<T> Eventually<T>(Func<T?> read) where T : class
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (read() is { } value)
            {
                return value;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("The value never arrived.");
    }
}
