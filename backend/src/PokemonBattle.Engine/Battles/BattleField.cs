namespace PokemonBattle.Engine.Battles;

/// <summary>Field state shared by every Pokémon in a battle (weather for now).</summary>
internal sealed class BattleField
{
    public Weather? Weather { get; set; }
}
