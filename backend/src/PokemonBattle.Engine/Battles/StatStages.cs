namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Generation 6 stage multipliers, following Pokémon Showdown's calculateStat and hitStepAccuracy.
/// </summary>
public static class StatStages
{
    public const int Min = -6;
    public const int Max = 6;

    /// <summary>Attack, Defense, Special Attack, Special Defense and Speed: x(2+n)/2 or x2/(2-n), floored.</summary>
    public static int ApplyToStat(int stat, int stage)
    {
        stage = Math.Clamp(stage, Min, Max);
        return stage >= 0 ? stat * (2 + stage) / 2 : stat * 2 / (2 - stage);
    }

    /// <summary>
    /// Move accuracy after the user's accuracy stage and the target's evasion stage. Both combine into
    /// one stage clamped to [-6, 6], applied as x(3+n)/3 or x3/(3-n) and truncated.
    /// </summary>
    public static int ApplyToAccuracy(int accuracy, int accuracyStage, int evasionStage)
    {
        var stage = Math.Clamp(Math.Clamp(accuracyStage, Min, Max) - evasionStage, Min, Max);
        return stage >= 0 ? accuracy * (3 + stage) / 3 : accuracy * 3 / (3 - stage);
    }
}
