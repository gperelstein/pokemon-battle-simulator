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

    [JsonPropertyName("selfSwitch")]
    public JsonElement? SelfSwitch { get; init; }

    [JsonIgnore]
    public SelfSwitchKind SelfSwitchKind => SelfSwitch switch
    {
        { ValueKind: JsonValueKind.True } => SelfSwitchKind.Switch,
        { ValueKind: JsonValueKind.String } s when s.GetString() == "copyvolatile" => SelfSwitchKind.CopyVolatile,
        _ => SelfSwitchKind.None,
    };

    /// <summary>Remaining declarative mechanics (secondary effects, boosts, drain...) kept raw.</summary>
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
