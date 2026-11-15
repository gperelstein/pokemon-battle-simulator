using Akka.Actor;
using PokemonBattle.Actors;
using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Api.Battles;

/// <summary>
/// Stands for one player in front of the <see cref="BattleActor"/>: hands each update to the session
/// in arrival order (the mailbox waits for each handler) and stops once the battle has ended.
/// </summary>
public sealed class PlayerRelay : ReceiveActor
{
    public PlayerRelay(Func<BattleUpdate, Task> handle)
    {
        ReceiveAsync<BattleUpdate>(async update =>
        {
            await handle(update);
            if (update.Events.Any(e => e is BattleEnded))
            {
                Context.Stop(Self);
            }
        });
    }

    public static Props Props(Func<BattleUpdate, Task> handle) => Akka.Actor.Props.Create(() => new PlayerRelay(handle));
}
