namespace PokemonBattle.Engine.Pokemon;

/// <summary>
/// Battle data of a held item, from the catalog (Showdown's item data). It travels with the item
/// when it changes hands (Trick, Thief, Knock Off, Recycle, Pickup), so a Plate keeps boosting its
/// type and Fling keeps its power on the new holder.
/// </summary>
/// <param name="Id">Item id, as in <see cref="PokemonSet.Item"/>.</param>
/// <param name="PlateType">Type the Plate boosts (Showdown's "onPlate"); null for other items.</param>
/// <param name="FlingPower">Fling's base power with this item (Showdown's fling.basePower); 0 makes Fling fail.</param>
/// <param name="FlingStatus">Status Fling inflicts on the target (Toxic Orb, Flame Orb, Light Ball, Poison Barb).</param>
/// <param name="FlingFlinches">
/// Fling makes the target flinch (King's Rock, Razor Fang: fling.volatileStatus). The engine has no
/// flinch yet, so it has no effect.
/// </param>
/// <param name="NaturalGiftType">Natural Gift's type with this berry; null when Natural Gift fails with it.</param>
/// <param name="NaturalGiftPower">Natural Gift's base power with this berry.</param>
/// <param name="BoundSpecies">
/// Base species that can neither lose nor receive the item (Showdown's onTakeItem): a Mega Stone's
/// species, Arceus for Plates, Genesect for Drives...
/// </param>
public sealed record ItemDefinition(
    string Id,
    PokemonType? PlateType = null,
    int FlingPower = 0,
    MajorStatus? FlingStatus = null,
    bool FlingFlinches = false,
    PokemonType? NaturalGiftType = null,
    int NaturalGiftPower = 0,
    IReadOnlyList<string>? BoundSpecies = null)
{
    public IReadOnlyList<string> BoundSpecies { get; init; } = BoundSpecies ?? [];
}
