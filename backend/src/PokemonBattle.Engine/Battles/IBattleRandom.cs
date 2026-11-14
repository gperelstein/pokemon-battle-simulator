namespace PokemonBattle.Engine.Battles;

/// <summary>Source of randomness, injectable so battles can be replayed and tested deterministically.</summary>
public interface IBattleRandom
{
    /// <summary>Returns an integer in [minInclusive, maxExclusive).</summary>
    int Next(int minInclusive, int maxExclusive);
}

public sealed class SeededBattleRandom(int seed) : IBattleRandom
{
    private readonly Random _random = new(seed);

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}
