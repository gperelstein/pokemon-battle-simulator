using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Catalog;

/// <summary>A team member as submitted by a player, referencing catalog entries by name or id.</summary>
public sealed record PokemonSetSpec(
    string Species,
    IReadOnlyList<string> Moves,
    string Ability,
    string? Item = null,
    string Nature = "Serious",
    int Level = 100,
    StatSpread? Evs = null,
    StatSpread? Ivs = null,
    int Happiness = 255);

/// <param name="Team">Engine-ready sets; empty when <paramref name="Errors"/> is not empty.</param>
public sealed record TeamBuildResult(IReadOnlyList<PokemonSet> Team, IReadOnlyList<string> Errors);

/// <summary>
/// Validates teams against Gen 6 game rules (legality of species, moves, abilities, items, natures,
/// levels, EVs and IVs) and turns them into engine <see cref="PokemonSet"/>s. Format clauses
/// (Species Clause, Item Clause, bans...) are not part of the game rules and are not applied here.
/// </summary>
public sealed class TeamBuilder(Gen6Catalog catalog)
{
    public const int MaxTeamSize = 6;
    public const int MaxEvPerStat = 252;
    public const int MaxEvTotal = 510;
    public const int MaxIv = 31;
    public const int MaxHappiness = 255;

    public TeamBuildResult Build(IReadOnlyList<PokemonSetSpec> team)
    {
        if (team.Count is 0 or > MaxTeamSize)
        {
            return new TeamBuildResult([], [$"A team must have between 1 and {MaxTeamSize} Pokémon."]);
        }

        var errors = new List<string>();
        var sets = new List<PokemonSet>();
        for (var index = 0; index < team.Count; index++)
        {
            var set = BuildMember(team[index], $"Slot {index + 1}", errors);
            if (set is not null)
            {
                sets.Add(set);
            }
        }

        return errors.Count == 0 ? new TeamBuildResult(sets, []) : new TeamBuildResult([], errors);
    }

    private PokemonSet? BuildMember(PokemonSetSpec spec, string label, List<string> errors)
    {
        var species = catalog.FindSpecies(spec.Species);
        if (species is null)
        {
            errors.Add($"{label}: species '{spec.Species}' is not available.");
            return null;
        }

        if (species.IsMega)
        {
            errors.Add($"{label}: {species.Name} can only appear by Mega Evolving in battle.");
            return null;
        }

        var firstError = errors.Count;
        var moves = ValidateMoves(spec, species, label, errors);

        var ability = catalog.FindAbility(spec.Ability);
        if (ability is null || !species.Abilities.Values.Contains(ability.Name))
        {
            errors.Add($"{label}: {species.Name} cannot have the ability '{spec.Ability}' " +
                       $"({string.Join(", ", species.Abilities.Values)}).");
        }

        var item = spec.Item is null ? null : catalog.FindItem(spec.Item);
        if (spec.Item is not null && item is null)
        {
            errors.Add($"{label}: item '{spec.Item}' does not exist in Gen 6.");
        }

        var nature = catalog.FindNature(spec.Nature);
        if (nature is null)
        {
            errors.Add($"{label}: nature '{spec.Nature}' does not exist.");
        }

        if (spec.Level is < 1 or > 100)
        {
            errors.Add($"{label}: level must be between 1 and 100.");
        }

        var evs = spec.Evs ?? StatSpread.All(0);
        if (evs.Values.Any(ev => ev is < 0 or > MaxEvPerStat) || evs.Values.Sum() > MaxEvTotal)
        {
            errors.Add($"{label}: EVs must be between 0 and {MaxEvPerStat} per stat and {MaxEvTotal} in total.");
        }

        var ivs = spec.Ivs ?? StatSpread.All(MaxIv);
        if (ivs.Values.Any(iv => iv is < 0 or > MaxIv))
        {
            errors.Add($"{label}: IVs must be between 0 and {MaxIv}.");
        }

        if (spec.Happiness is < 0 or > MaxHappiness)
        {
            errors.Add($"{label}: happiness must be between 0 and {MaxHappiness}.");
        }

        if (errors.Count > firstError)
        {
            return null;
        }

        return new PokemonSet(
            species.Name,
            species.Types,
            spec.Level,
            StatCalculator.Calculate(species.BaseStats, spec.Level, ivs, evs, nature!),
            moves.Select(m => m.ToDefinition()).ToArray(),
            ability!.Id,
            item?.Id,
            (int)Math.Round(species.Weightkg * 10),
            spec.Happiness,
            ability.Flags.Contains("breakable"),
            !ability.Flags.Contains("notrace"),
            species.BaseSpecies,
            species.Evos.Count > 0,
            item?.OnPlate,
            item?.ToDefinition(),
            MegaFormeOf(species, item, spec.Level, ivs, evs, nature!));
    }

    /// <summary>
    /// The Mega Evolution the held item allows (Showdown's canMegaEvo: item.megaStone keyed by the
    /// holder's base species), with stats from the same level, IVs, EVs and nature; null otherwise.
    /// </summary>
    private MegaForme? MegaFormeOf(SpeciesData species, ItemData? item, int level, StatSpread ivs, StatSpread evs, NatureData nature)
    {
        if (item?.MegaStone is not { } stone || !stone.TryGetValue(species.BaseSpecies, out var megaName) ||
            megaName == species.Name || catalog.FindSpecies(megaName) is not { } mega ||
            catalog.FindAbility(mega.Abilities["0"]) is not { } ability)
        {
            return null;
        }

        return new MegaForme(
            mega.Name,
            item.Id,
            mega.Types,
            StatCalculator.Calculate(mega.BaseStats, level, ivs, evs, nature),
            ability.Id,
            (int)Math.Round(mega.Weightkg * 10),
            ability.Flags.Contains("breakable"),
            !ability.Flags.Contains("notrace"));
    }

    private List<MoveData> ValidateMoves(PokemonSetSpec spec, SpeciesData species, string label, List<string> errors)
    {
        var moves = new List<MoveData>();
        if (spec.Moves.Count is 0 or > PokemonSet.MaxMoves)
        {
            errors.Add($"{label}: must know between 1 and {PokemonSet.MaxMoves} moves.");
            return moves;
        }

        foreach (var name in spec.Moves)
        {
            var move = catalog.FindMove(name);
            if (move is null)
            {
                errors.Add($"{label}: move '{name}' does not exist in Gen 6.");
            }
            else if (!catalog.CanLearn(species.Id, move.Id))
            {
                errors.Add($"{label}: {species.Name} cannot learn {move.Name}.");
            }
            else if (moves.Contains(move))
            {
                errors.Add($"{label}: {move.Name} is repeated.");
            }
            else
            {
                moves.Add(move);
            }
        }

        return moves;
    }
}
