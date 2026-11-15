using Akka.Actor;
using PokemonBattle.Catalog;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Api.Battles;

public static class BattleServices
{
    /// <summary>
    /// Gen 6 catalog, team builder, the actor system hosting battles, the battle registry and the
    /// SignalR hub that delivers updates.
    /// </summary>
    public static IServiceCollection AddBattles(this IServiceCollection services)
    {
        services.AddSignalR().AddJsonProtocol(options => ApiJson.Configure(options.PayloadSerializerOptions));
        services.AddSingleton<IPlayerNotifier, HubPlayerNotifier>();
        services.AddSingleton(Gen6Catalog.Instance);
        services.AddSingleton<ITypeChart>(provider => provider.GetRequiredService<Gen6Catalog>().TypeChart);
        services.AddSingleton<TeamBuilder>();
        services.AddSingleton(_ => ActorSystem.Create("battles"));
        services.AddHostedService<ActorSystemShutdown>();
        services.AddSingleton<BattleRegistry>();
        return services;
    }

    /// <summary>Terminates the actor system (and every hosted battle) when the host stops.</summary>
    private sealed class ActorSystemShutdown(ActorSystem system) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => system.Terminate();
    }
}
