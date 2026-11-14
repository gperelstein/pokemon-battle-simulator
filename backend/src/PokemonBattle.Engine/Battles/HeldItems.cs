using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Passive held-item modifiers, following Showdown's data/items.ts. Gen 6 is master overridden by
/// the gen8, gen7 and gen6 item mods, which leave these handlers alone (Life Orb's recoil rule is
/// the gen6 one, see <see cref="Battle"/>). Each helper reads <see cref="PokemonSet.Item"/>, so a
/// set whose item was consumed (Normal Gem) must carry a null item. Items are ignored by no ability
/// in the Gen 1 catalog (no Klutz), so no suppression check is needed yet.
/// </summary>
internal static class HeldItems
{
    public const string LifeOrb = "lifeorb";
    public const string IronBall = "ironball";
    public const string NormalGem = "normalgem";
    public const string Metronome = "metronome";
    public const string QuickClaw = "quickclaw";

    /// <summary>Type-boosting items without catalog type data (onBasePower, priority 15, x4915/4096).</summary>
    private static readonly Dictionary<string, PokemonType> TypeBoosters = new()
    {
        ["silverpowder"] = PokemonType.Bug,
        ["metalcoat"] = PokemonType.Steel,
        ["softsand"] = PokemonType.Ground,
        ["hardstone"] = PokemonType.Rock,
        ["miracleseed"] = PokemonType.Grass,
        ["blackglasses"] = PokemonType.Dark,
        ["blackbelt"] = PokemonType.Fighting,
        ["magnet"] = PokemonType.Electric,
        ["mysticwater"] = PokemonType.Water,
        ["sharpbeak"] = PokemonType.Flying,
        ["poisonbarb"] = PokemonType.Poison,
        ["nevermeltice"] = PokemonType.Ice,
        ["spelltag"] = PokemonType.Ghost,
        ["twistedspoon"] = PokemonType.Psychic,
        ["charcoal"] = PokemonType.Fire,
        ["dragonfang"] = PokemonType.Dragon,
        ["silkscarf"] = PokemonType.Normal,
        ["seaincense"] = PokemonType.Water,
        ["waveincense"] = PokemonType.Water,
        ["oddincense"] = PokemonType.Psychic,
        ["rockincense"] = PokemonType.Rock,
        ["roseincense"] = PokemonType.Grass,
    };

    /// <summary>Consecutive-use multipliers of the Metronome item, in 4096ths (capped at 5 uses).</summary>
    private static readonly int[] MetronomeModifiers = [4096, 4915, 5734, 6553, 7372, 8192];

    /// <summary>
    /// BasePower handlers of the holder's item, in priority order: Muscle Band / Wise Glasses (16,
    /// x4505/4096), then type boosters and Plates (15, x4915/4096).
    /// </summary>
    public static ModifierChain ChainBasePower(ModifierChain chain, PokemonSet user, MoveDefinition move)
    {
        if ((user.Item, move.Category) is ("muscleband", MoveCategory.Physical) or ("wiseglasses", MoveCategory.Special))
        {
            chain = chain.Chain(4505, 4096);
        }

        return BoostedType(user) == move.Type ? chain.Chain(4915, 4096) : chain;
    }

    /// <summary>Type boosted by the held item: a Plate's catalog type or a type-boosting item's.</summary>
    private static PokemonType? BoostedType(PokemonSet set) => set.Item is null
        ? null
        : set.PlateType ?? (TypeBoosters.TryGetValue(set.Item, out var type) ? type : null);

    /// <summary>
    /// ModifyAtk / ModifySpA of the attacker's item (priority 1, after the abilities): Choice Band
    /// (Attack) and Choice Specs (Special Attack) x1.5, Light Ball x2 on any Pikachu (both stats),
    /// Thick Club x2 on Cubone and Marowak (Attack).
    /// </summary>
    public static ModifierChain ChainAttack(ModifierChain chain, PokemonSet attacker, bool physical)
    {
        var baseSpecies = ToId(attacker.BaseSpecies ?? attacker.Species);
        return (attacker.Item, physical) switch
        {
            ("choiceband", true) or ("choicespecs", false) => chain.Chain(3, 2),
            ("lightball", _) when baseSpecies == "pikachu" => chain.Chain(2),
            ("thickclub", true) when baseSpecies is "cubone" or "marowak" => chain.Chain(2),
            _ => chain,
        };
    }

    /// <summary>
    /// ModifyDef / ModifySpD of the defender's item: Eviolite x1.5 (both, if the species can still
    /// evolve) and Metal Powder x2 on Ditto (Defense) at priority 2, Assault Vest x1.5 (Special Defense)
    /// at priority 1. Metal Powder's "not transformed" check waits for Transform.
    /// </summary>
    public static ModifierChain ChainDefense(ModifierChain chain, PokemonSet defender, bool physical) =>
        (defender.Item, physical) switch
        {
            ("eviolite", _) when defender.CanEvolve => chain.Chain(3, 2),
            ("metalpowder", true) when defender.Species == "Ditto" => chain.Chain(2),
            ("assaultvest", false) => chain.Chain(3, 2),
            _ => chain,
        };

    /// <summary>
    /// ModifySpe of the holder's item (priority 0, after the abilities): Choice Scarf x1.5, Quick
    /// Powder x2 on Ditto, Macho Brace, the Power items and Iron Ball x0.5.
    /// </summary>
    public static ModifierChain ChainSpeed(ModifierChain chain, string? item, string species) => item switch
    {
        "choicescarf" => chain.Chain(3, 2),
        "quickpowder" when species == "Ditto" => chain.Chain(2),
        "machobrace" or "powerweight" or "powerbracer" or "powerbelt" or "powerlens" or "powerband" or "poweranklet"
            or IronBall => chain.Chain(1, 2),
        _ => chain,
    };

    /// <summary>
    /// ModifyCritRatio of the attacker's item: Scope Lens and Razor Claw +1, Lucky Punch on Chansey
    /// and Stick on Farfetch'd +2.
    /// </summary>
    public static int CritStageBonus(PokemonSet attacker) => attacker.Item switch
    {
        "scopelens" or "razorclaw" => 1,
        "luckypunch" when attacker.Species == "Chansey" => 2,
        "stick" when ToId(attacker.BaseSpecies ?? attacker.Species) == "farfetchd" => 2,
        _ => 0,
    };

    /// <summary>
    /// ModifyDamage of the attacker's item: Expert Belt x4915/4096 on super-effective hits, Life Orb
    /// x5324/4096. Runs after the attacker's volatile conditions and abilities (Showdown's subOrder).
    /// </summary>
    public static ModifierChain ChainDamage(ModifierChain chain, PokemonSet attacker, double effectiveness) =>
        attacker.Item switch
        {
            "expertbelt" when effectiveness > 1 => chain.Chain(4915, 4096),
            LifeOrb => chain.Chain(5324, 4096),
            _ => chain,
        };

    /// <summary>Metronome item condition (onModifyDamage): x1.2 per consecutive use, up to x2.</summary>
    public static ModifierChain ChainMetronome(ModifierChain chain, PokemonSet attacker, int consecutiveUses) =>
        attacker.Item == Metronome
            ? chain.Chain(MetronomeModifiers[Math.Clamp(consecutiveUses, 0, MetronomeModifiers.Length - 1)], 4096)
            : chain;

    /// <summary>
    /// Accuracy modifier (priority -2) of the user's item: Wide Lens x4505/4096, Zoom Lens x4915/4096
    /// when the target has already moved this turn.
    /// </summary>
    public static int? UserAccuracyModifier(string? item, bool targetAlreadyMoved) => item switch
    {
        "widelens" => 4505,
        "zoomlens" when targetAlreadyMoved => 4915,
        _ => null,
    };

    /// <summary>Accuracy modifier (priority -2) of the target's item: Bright Powder and Lax Incense x3686/4096.</summary>
    public static int? TargetAccuracyModifier(string? item) => item is "brightpowder" or "laxincense" ? 3686 : null;

    /// <summary>
    /// Fractional priority inside the move's priority bracket (onFractionalPriority): Lagging Tail and
    /// Full Incense -0.1 (move last). Quick Claw's +0.1 is rolled by the battle.
    /// </summary>
    public static int FractionalPriority(string? item) => item is "laggingtail" or "fullincense" ? -1 : 0;

    /// <summary>
    /// The types that count against a move: none for a Flying Iron Ball holder hit by a Ground move,
    /// which therefore takes neutral damage (Iron Ball's onEffectiveness).
    /// </summary>
    public static IReadOnlyList<PokemonType> DefendingTypes(PokemonSet defender, PokemonType moveType) =>
        // Showdown's Iron Ball onEffectiveness returns 0 for EVERY type of a Flying holder hit by a
        // Ground move, and runEffectiveness sums them: the hit is neutral whatever the other type is
        // (data_items.ts ironball, sim/pokemon.ts runEffectiveness). No type left = neutral.
        defender.Item == IronBall && moveType == PokemonType.Ground && defender.Types.Contains(PokemonType.Flying)
            ? []
            : defender.Types;

    /// <summary>Showdown's toID: lowercase letters and digits only (Farfetch’d -> farfetchd).</summary>
    private static string ToId(string name) =>
        new(name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
}
