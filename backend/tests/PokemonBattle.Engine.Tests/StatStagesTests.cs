using PokemonBattle.Engine.Battles;

namespace PokemonBattle.Engine.Tests;

/// <summary>Gen 6 stage multipliers, checked against Pokémon Showdown's calculateStat and hitStepAccuracy.</summary>
public class StatStagesTests
{
    [Theory]
    [InlineData(100, 2, 200)]
    [InlineData(100, 6, 400)]
    [InlineData(255, 1, 382)] // 382.5 floors
    [InlineData(101, -1, 67)] // 101 / 1.5 = 67.33 floors
    [InlineData(99, -2, 49)] // 99 / 2 = 49.5 floors
    [InlineData(100, -6, 25)]
    [InlineData(100, 0, 100)]
    public void Stat_stages_multiply_by_two_plus_n_over_two(int stat, int stage, int expected) =>
        Assert.Equal(expected, StatStages.ApplyToStat(stat, stage));

    [Theory]
    [InlineData(100, -1, 0, 75)]
    [InlineData(100, 0, 1, 75)]
    [InlineData(100, 1, 1, 100)]
    [InlineData(70, 1, 0, 93)] // 93.33 truncates
    [InlineData(85, 0, 2, 51)]
    [InlineData(100, 6, 0, 300)]
    [InlineData(100, -6, 0, 33)]
    [InlineData(100, -6, 6, 33)] // the combined stage is clamped to -6
    public void Accuracy_uses_the_accuracy_stage_minus_the_evasion_stage(
        int accuracy, int accuracyStage, int evasionStage, int expected) =>
        Assert.Equal(expected, StatStages.ApplyToAccuracy(accuracy, accuracyStage, evasionStage));
}
