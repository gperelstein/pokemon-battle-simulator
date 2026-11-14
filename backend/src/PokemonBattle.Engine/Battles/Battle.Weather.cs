using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Field weather, following Showdown's data/conditions.ts: 5 turns counting the turn it starts
/// (the counter drops at each end of turn and the weather ends when it reaches 0), with sandstorm
/// and hail dealing 1/16 of max HP to non-immune Pokémon, by speed, before status damage.
/// </summary>
public sealed partial class Battle
{
    private const int WeatherDuration = 5;
    private int _weatherTurnsLeft;

    /// <summary>Current weather, or null for clear skies.</summary>
    public Weather? Weather { get; private set; }

    /// <summary>Starts a weather. Returns false when that weather is already active.</summary>
    private bool SetWeather(Weather weather)
    {
        if (Weather == weather)
        {
            return false;
        }

        Weather = weather;
        _weatherTurnsLeft = WeatherDuration;
        _events.Add(new WeatherStarted(weather));
        return true;
    }

    private void ApplyWeatherResidual()
    {
        if (Weather is not { } weather)
        {
            return;
        }

        if (--_weatherTurnsLeft == 0)
        {
            Weather = null;
            _events.Add(new WeatherEnded(weather));
            return;
        }

        _events.Add(new WeatherContinues(weather));
        var condition = weather switch
        {
            Battles.Weather.Sandstorm => TypeCondition.Sandstorm,
            Battles.Weather.Hail => TypeCondition.Hail,
            _ => (TypeCondition?)null,
        };
        if (condition is null)
        {
            return;
        }

        var hurt = _sides.Values
            .SelectMany(side => side.LiveSlots.Select(slot => side.Active[slot]!))
            .Where(p => !_typeChart.IsImmune(p.Set.Types, condition.Value))
            .OrderByDescending(p => p.Speed)
            .ToArray();

        foreach (var pokemon in hurt)
        {
            var (side, slot) = Locate(pokemon);
            var dealt = pokemon.TakeDamage(Math.Max(1, pokemon.Set.Stats.Hp / 16));
            pokemon.HurtThisTurn = true;
            _events.Add(new WeatherDamage(side.Id, slot, pokemon.Set.Species, weather, dealt, pokemon.CurrentHp));
            if (pokemon.IsFainted)
            {
                _events.Add(new Fainted(side.Id, slot, pokemon.Set.Species));
            }
        }
    }
}
