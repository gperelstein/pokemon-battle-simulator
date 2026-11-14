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
    public Weather? Weather
    {
        get => _field.Weather;
        private set => _field.Weather = value;
    }

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

        // Each active Pokémon's weather effect, by speed (Showdown's eachEvent('Weather')).
        var active = _sides.Values
            .SelectMany(side => side.LiveSlots.Select(slot => side.Active[slot]!))
            .OrderByDescending(p => p.Speed)
            .ToArray();

        foreach (var pokemon in active)
        {
            // Dry Skin recovers 1/8 in rain (nothing at full HP).
            if (weather == Battles.Weather.Rain && pokemon.Set.Ability == "dryskin")
            {
                HealByAbility(pokemon, "dryskin", pokemon.Set.Stats.Hp / 8);
                continue;
            }

            var damage = WeatherDamageTo(pokemon, weather);
            if (damage == 0)
            {
                continue;
            }

            var (side, slot) = Locate(pokemon);
            var dealt = pokemon.TakeDamage(damage);
            pokemon.HurtThisTurn = true;
            _events.Add(new WeatherDamage(side.Id, slot, pokemon.Set.Species, weather, dealt, pokemon.CurrentHp));
            if (pokemon.IsFainted)
            {
                _events.Add(new Fainted(side.Id, slot, pokemon.Set.Species));
            }
        }
    }

    /// <summary>
    /// End-of-turn weather damage to one Pokémon: 1/16 from sandstorm or hail unless its type, its
    /// ability (Sand Veil, Sand Rush, Sand Force, Overcoat; Snow Cloak, Overcoat) or Dig/Dive shelters
    /// it, and 1/8 in sun for Solar Power and Dry Skin holders. Magic Guard blocks all of it.
    /// </summary>
    private int WeatherDamageTo(BattlePokemon pokemon, Weather weather)
    {
        var maxHp = pokemon.Set.Stats.Hp;
        var ability = pokemon.Set.Ability;
        if (IgnoresIndirectDamage(pokemon))
        {
            return 0;
        }

        switch (weather)
        {
            case Battles.Weather.Sun when ability is "solarpower" or "dryskin":
                return Math.Max(1, maxHp / 8);

            case Battles.Weather.Sandstorm:
                var sandImmune = _typeChart.IsImmune(pokemon.Set.Types, TypeCondition.Sandstorm) ||
                    ability is "sandveil" or "sandrush" or "sandforce" or "overcoat";
                return sandImmune || IsShelteredFromWeather(pokemon) ? 0 : Math.Max(1, maxHp / 16);

            case Battles.Weather.Hail:
                var hailImmune = _typeChart.IsImmune(pokemon.Set.Types, TypeCondition.Hail) ||
                    ability is "snowcloak" or "overcoat";
                return hailImmune || IsShelteredFromWeather(pokemon) ? 0 : Math.Max(1, maxHp / 16);

            default:
                return 0;
        }
    }
}
