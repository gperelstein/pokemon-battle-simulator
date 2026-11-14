namespace PokemonBattle.Engine.Pokemon;

/// <summary>Final (already calculated) stats of a Pokémon as it enters battle.</summary>
public sealed record Stats(int Hp, int Attack, int Defense, int SpecialAttack, int SpecialDefense, int Speed);
