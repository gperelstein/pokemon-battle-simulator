using PokemonBattle.Catalog;
using PokemonBattle.Engine.Battles;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Api.Battles;

/// <summary>
/// HTTP lobby for anonymous players (S6): create a battle, join it, read its public status. Teams
/// always go through <see cref="TeamBuilder"/> (L37); invalid teams get a validation problem whose
/// "team" entry lists the builder's errors.
/// </summary>
public static class LobbyEndpoints
{
    public static IEndpointRouteBuilder MapLobby(this IEndpointRouteBuilder app)
    {
        app.MapPost("/battles", (CreateBattleBody body, TeamBuilder builder, BattleRegistry registry) =>
        {
            var (team, errors) = BuildTeam(body.Team, body.Format, builder);
            if (errors is not null)
            {
                return TeamProblem(errors);
            }

            var ticket = registry.Create(body.Format, team!);
            return Results.Created($"/battles/{ticket.BattleId}", ticket);
        });

        app.MapPost("/battles/{battleId:guid}/join", (Guid battleId, JoinBattleBody body, TeamBuilder builder, BattleRegistry registry) =>
        {
            if (registry.Find(battleId) is not { } session)
            {
                return Results.NotFound();
            }

            var (team, errors) = BuildTeam(body.Team, session.Format, builder);
            if (errors is not null)
            {
                return TeamProblem(errors);
            }

            return session.Join(team!) is { } ticket
                ? Results.Ok(ticket)
                : Results.Problem("The battle already has two players.", statusCode: StatusCodes.Status409Conflict);
        });

        app.MapGet("/battles/{battleId:guid}", (Guid battleId, BattleRegistry registry) =>
            registry.Find(battleId) is { } session
                ? Results.Ok(new BattleSummary(session.Id, session.Format, session.Status.ToString().ToLowerInvariant()))
                : Results.NotFound());

        return app;
    }

    private static (IReadOnlyList<PokemonSet>? Team, IReadOnlyList<string>? Errors) BuildTeam(
        IReadOnlyList<PokemonSetSpec>? specs, BattleFormat format, TeamBuilder builder)
    {
        if (specs is null || specs.Any(spec => spec is null || spec.Species is null || spec.Moves is null || spec.Ability is null))
        {
            return (null, ["Every Pokémon needs a species, moves and an ability."]);
        }

        var slots = format.ActiveSlots();
        if (specs.Count < slots)
        {
            return (null, [$"A {format.ToString().ToLowerInvariant()} team needs at least {slots} Pokémon."]);
        }

        var result = builder.Build(specs);
        return result.Errors.Count > 0 ? (null, result.Errors) : (result.Team, null);
    }

    private static IResult TeamProblem(IReadOnlyList<string> errors) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["team"] = errors.ToArray() });
}
