using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Two-turn moves, following Showdown's twoturnmove condition and each move's onTryMove: the
/// user announces the move, charges, and attacks the slot it chose on the next turn without its
/// player being asked (the choice is locked). Fly, Bounce, Dig, Dive, Shadow Force and Phantom
/// Force also make the user semi-invulnerable while charging.
/// </summary>
public sealed partial class Battle
{
    private static readonly string[] ReachesTheSky =
        ["gust", "twister", "skyuppercut", "thunder", "hurricane", "smackdown", "thousandarrows"];

    /// <summary>
    /// Starts charging unless the move fires at once (Solar Beam in sun). Returns true when the
    /// move charged, so the attack waits for the next turn.
    /// </summary>
    private bool TryStartCharging(MoveAction action, MoveDefinition move)
    {
        if (move.Id == "solarbeam" && EffectiveWeather == Battles.Weather.Sun)
        {
            return false;
        }

        var user = action.Actor;
        _events.Add(new MoveCharging(action.Side.Id, action.Slot, user.Set.Species, move.Id));
        if (move.Id == "skullbash")
        {
            ChangeStages(user, [new StatChange(BoostStat.Defense, 1)], announceLimit: true);
        }

        user.StartCharging(move.Id, action.Choice);
        return true;
    }

    /// <summary>Whether a semi-invulnerable target cannot be reached by the move.</summary>
    private static bool IsOutOfReach(BattlePokemon target, MoveDefinition move) => target.ChargingMoveId switch
    {
        "fly" or "bounce" => !ReachesTheSky.Contains(move.Id),
        "dig" => move.Id is not ("earthquake" or "magnitude"),
        "dive" => move.Id is not ("surf" or "whirlpool"),
        "shadowforce" or "phantomforce" => true,
        _ => false,
    };

    /// <summary>
    /// Final damage multiplier against a semi-invulnerable target (Showdown's onSourceModifyDamage):
    /// Gust and Twister against Fly, Earthquake and Magnitude against Dig, Surf and Whirlpool against
    /// Dive deal double damage. Bounce doubles base power instead (see <see cref="BasePower"/>).
    /// </summary>
    private static int SemiInvulnerableDamageMultiplier(BattlePokemon target, MoveDefinition move) =>
        (target.ChargingMoveId, move.Id) switch
        {
            ("fly", "gust" or "twister") => 2,
            ("dig", "earthquake" or "magnitude") => 2,
            ("dive", "surf" or "whirlpool") => 2,
            _ => 1,
        };

    /// <summary>Digging and diving Pokémon are sheltered from sandstorm and hail damage.</summary>
    private static bool IsShelteredFromWeather(BattlePokemon pokemon) => pokemon.ChargingMoveId is "dig" or "dive";
}
