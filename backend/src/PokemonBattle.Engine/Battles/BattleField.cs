namespace PokemonBattle.Engine.Battles;

/// <summary>Field state shared by every Pokémon in a battle (weather for now).</summary>
internal sealed class BattleField
{
    public Weather? Weather { get; set; }

    /// <summary>Whether something on the field cancels weather effects (Cloud Nine).</summary>
    public Func<bool> WeatherSuppressed { get; set; } = () => false;

    /// <summary>Weather as its effects see it (Showdown's effectiveWeather): null while suppressed.</summary>
    public Weather? EffectiveWeather => WeatherSuppressed() ? null : Weather;
}
