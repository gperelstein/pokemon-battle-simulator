namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Stacked multipliers combined the way the games do (Showdown's chainModify and finalModify):
/// each multiplier becomes a 4096-based fixed-point value, they are combined one by one with
/// rounding, and the result is applied once, rounding exact halves down.
/// </summary>
public readonly struct ModifierChain
{
    private const int One = 4096;
    private readonly int? _modifier;

    private ModifierChain(int modifier) => _modifier = modifier;

    /// <summary>Combined multiplier in 4096ths (4096 = x1).</summary>
    public int Modifier => _modifier ?? One;

    /// <summary>Adds a multiplier of <paramref name="numerator"/> / <paramref name="denominator"/>.</summary>
    public ModifierChain Chain(int numerator, int denominator = 1)
    {
        var next = (long)numerator * One / denominator;
        return new ModifierChain((int)((Modifier * next + 2048) >> 12));
    }

    /// <summary>Applies the combined multiplier to <paramref name="value"/>.</summary>
    public int Apply(int value) => (int)(((long)value * Modifier + 2047) / One);
}
