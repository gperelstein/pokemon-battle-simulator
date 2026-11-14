using System.Text.Json;
using System.Text.Json.Serialization;
using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Catalog;

public sealed record BaseStats(int Hp, int Atk, int Def, int Spa, int Spd, int Spe);

public sealed record SpeciesData
{
    public required string Id { get; init; }
    public required int Num { get; init; }
    public required string Name { get; init; }
    public required string BaseSpecies { get; init; }
    public string? Forme { get; init; }
    public required IReadOnlyList<PokemonType> Types { get; init; }
    public required BaseStats BaseStats { get; init; }

    /// <summary>Ability slots as in Showdown: "0", "1" and "H" (hidden).</summary>
    public required IReadOnlyDictionary<string, string> Abilities { get; init; }

    public double Weightkg { get; init; }
    public bool IsMega { get; init; }

    /// <summary>For Mega Evolutions, the stone that triggers them.</summary>
    public string? RequiredItem { get; init; }
}

public enum SelfSwitchKind
{
    None,

    /// <summary>U-turn, Volt Switch, Parting Shot...</summary>
    Switch,

    /// <summary>Baton Pass: the replacement inherits stat stages and volatile effects.</summary>
    CopyVolatile,
}

public sealed record MoveData
{
    public required string Id { get; init; }
    public required int Num { get; init; }
    public required string Name { get; init; }
    public required PokemonType Type { get; init; }
    public required MoveCategory Category { get; init; }
    public int BasePower { get; init; }

    /// <summary>Null for moves that never miss.</summary>
    public int? Accuracy { get; init; }

    public int Pp { get; init; }
    public int Priority { get; init; }

    /// <summary>Showdown target kind (normal, allAdjacentFoes, self...).</summary>
    public required string Target { get; init; }

    public IReadOnlyList<string> Flags { get; init; } = [];
    public string? ShortDesc { get; init; }

    /// <summary>Critical hit stage: 1 = 1/16, 2 = 1/8, 3 = 1/2, 4 or more = always.</summary>
    public int CritRatio { get; init; } = 1;

    public bool WillCrit { get; init; }

    /// <summary>Fixed damage: a number (Dragon Rage) or the string "level" (Seismic Toss).</summary>
    [JsonPropertyName("damage")]
    public JsonElement? Damage { get; init; }

    [JsonIgnore]
    public int? FixedDamage => Damage is { ValueKind: JsonValueKind.Number } d ? d.GetInt32() : null;

    [JsonIgnore]
    public bool LevelDamage => Damage is { ValueKind: JsonValueKind.String } d && d.GetString() == "level";

    [JsonPropertyName("selfSwitch")]
    public JsonElement? SelfSwitch { get; init; }

    [JsonIgnore]
    public SelfSwitchKind SelfSwitchKind => SelfSwitch switch
    {
        { ValueKind: JsonValueKind.True } => SelfSwitchKind.Switch,
        { ValueKind: JsonValueKind.String } s when s.GetString() == "copyvolatile" => SelfSwitchKind.CopyVolatile,
        _ => SelfSwitchKind.None,
    };

    /// <summary>Stage changes applied to the target (keys: atk, def, spa, spd, spe, accuracy, evasion).</summary>
    public IReadOnlyDictionary<string, int>? Boosts { get; init; }

    /// <summary>Effect on the user after hitting (Close Combat's drops, Diamond Storm's 50% boost...).</summary>
    public MoveSelfData? Self { get; init; }

    public IReadOnlyList<MoveSecondaryData>? Secondaries { get; init; }

    public bool IgnoreEvasion { get; init; }
    public bool IgnoreDefensive { get; init; }

    /// <summary>Remaining declarative mechanics (status, drain, recoil...) kept raw.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Effects { get; init; } = [];

    /// <summary>
    /// Engine view of the move. Stat changes are mapped; secondary effects that only inflict a
    /// status or volatile condition are left out until those mechanics exist.
    /// </summary>
    public MoveDefinition ToDefinition() => new(
        Id,
        Name,
        Type,
        Category,
        BasePower,
        Priority,
        Enum.Parse<MoveTarget>(Target, ignoreCase: true),
        SelfSwitchKind != SelfSwitchKind.None,
        CritRatio,
        WillCrit,
        FixedDamage,
        LevelDamage,
        Accuracy,
        ToStatChanges(Boosts),
        ToStatChanges(Self?.Boosts),
        Self?.Boosts is null ? null : Self.Chance,
        Secondaries?
            .Select(s => new SecondaryEffect(s.Chance ?? 100, ToStatChanges(s.Boosts), ToStatChanges(s.Self?.Boosts)))
            .Where(s => s.TargetBoosts.Count > 0 || s.SelfBoosts.Count > 0)
            .ToArray(),
        IgnoreEvasion,
        IgnoreDefensive);

    private static StatChange[] ToStatChanges(IReadOnlyDictionary<string, int>? boosts) =>
        boosts?.Select(entry => new StatChange(ToBoostStat(entry.Key), entry.Value)).ToArray() ?? [];

    private static BoostStat ToBoostStat(string key) => key switch
    {
        "atk" => BoostStat.Attack,
        "def" => BoostStat.Defense,
        "spa" => BoostStat.SpecialAttack,
        "spd" => BoostStat.SpecialDefense,
        "spe" => BoostStat.Speed,
        "accuracy" => BoostStat.Accuracy,
        "evasion" => BoostStat.Evasion,
        _ => throw new InvalidOperationException($"Unknown boost stat '{key}'."),
    };
}

public sealed record MoveSelfData
{
    public IReadOnlyDictionary<string, int>? Boosts { get; init; }

    /// <summary>Percentage chance; null means always.</summary>
    public int? Chance { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Effects { get; init; } = [];
}

public sealed record MoveSecondaryData
{
    /// <summary>Percentage chance; null means always.</summary>
    public int? Chance { get; init; }

    public IReadOnlyDictionary<string, int>? Boosts { get; init; }
    public MoveSelfData? Self { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Effects { get; init; } = [];
}

public sealed record AbilityData
{
    public required string Id { get; init; }
    public required int Num { get; init; }
    public required string Name { get; init; }
    public string? ShortDesc { get; init; }
    public IReadOnlyList<string> Flags { get; init; } = [];
}

public sealed record ItemData
{
    public required string Id { get; init; }
    public required int Num { get; init; }
    public required string Name { get; init; }
    public string? ShortDesc { get; init; }

    /// <summary>Base species name to Mega Evolution name, for Mega Stones.</summary>
    public IReadOnlyDictionary<string, string>? MegaStone { get; init; }

    public bool IsBerry { get; init; }
    public bool IsChoice { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Effects { get; init; } = [];
}

public sealed record NatureData
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Raised stat (atk, def, spa, spd, spe) or null for neutral natures.</summary>
    public string? Plus { get; init; }

    /// <summary>Lowered stat or null for neutral natures.</summary>
    public string? Minus { get; init; }
}
