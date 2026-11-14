using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Catalog;

/// <summary>Individual values or effort values, one per stat.</summary>
public sealed record StatSpread(int Hp, int Atk, int Def, int Spa, int Spd, int Spe)
{
    public static StatSpread All(int value) => new(value, value, value, value, value, value);

    public IEnumerable<int> Values => [Hp, Atk, Def, Spa, Spd, Spe];
}

/// <summary>Stat formula used since Generation 3.</summary>
public static class StatCalculator
{
    public static Stats Calculate(BaseStats baseStats, int level, StatSpread ivs, StatSpread evs, NatureData nature)
    {
        var hp = (2 * baseStats.Hp + ivs.Hp + evs.Hp / 4) * level / 100 + level + 10;
        return new Stats(
            hp,
            Other(baseStats.Atk, ivs.Atk, evs.Atk, level, nature, "atk"),
            Other(baseStats.Def, ivs.Def, evs.Def, level, nature, "def"),
            Other(baseStats.Spa, ivs.Spa, evs.Spa, level, nature, "spa"),
            Other(baseStats.Spd, ivs.Spd, evs.Spd, level, nature, "spd"),
            Other(baseStats.Spe, ivs.Spe, evs.Spe, level, nature, "spe"));
    }

    private static int Other(int baseStat, int iv, int ev, int level, NatureData nature, string stat)
    {
        var value = (2 * baseStat + iv + ev / 4) * level / 100 + 5;
        var natureModifier = stat == nature.Plus ? 110 : stat == nature.Minus ? 90 : 100;
        return value * natureModifier / 100;
    }
}
