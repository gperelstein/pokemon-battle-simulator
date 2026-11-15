namespace PokemonBattle.Engine.Battles;

/// <summary>The Gen 6 berries (Showdown's isBerry items), which Harvest can restore and Unnerve stops.</summary>
public static class Berries
{
    public const string Cheri = "cheriberry";
    public const string Chesto = "chestoberry";
    public const string Pecha = "pechaberry";
    public const string Rawst = "rawstberry";
    public const string Aspear = "aspearberry";
    public const string Leppa = "leppaberry";
    public const string Oran = "oranberry";
    public const string Persim = "persimberry";
    public const string Lum = "lumberry";
    public const string Sitrus = "sitrusberry";
    public const string Chilan = "chilanberry";
    public const string Liechi = "liechiberry";
    public const string Ganlon = "ganlonberry";
    public const string Salac = "salacberry";
    public const string Petaya = "petayaberry";
    public const string Apicot = "apicotberry";
    public const string Lansat = "lansatberry";
    public const string Starf = "starfberry";
    public const string Enigma = "enigmaberry";
    public const string Micle = "micleberry";
    public const string Custap = "custapberry";
    public const string Jaboca = "jabocaberry";
    public const string Rowap = "rowapberry";
    public const string Kee = "keeberry";
    public const string Maranga = "marangaberry";

    /// <summary>Gen 6 healing berries that heal 1/8 of max HP at half HP (gen6 mod; they also confuse by nature).</summary>
    internal static readonly HashSet<string> FlavorHealers = ["figyberry", "wikiberry", "magoberry", "aguavberry", "iapapaberry"];

    /// <summary>Berries that halve a super-effective hit of their type (onSourceModifyDamage).</summary>
    internal static readonly Dictionary<string, Pokemon.PokemonType> TypeResist = new()
    {
        ["occaberry"] = Pokemon.PokemonType.Fire,
        ["passhoberry"] = Pokemon.PokemonType.Water,
        ["wacanberry"] = Pokemon.PokemonType.Electric,
        ["rindoberry"] = Pokemon.PokemonType.Grass,
        ["yacheberry"] = Pokemon.PokemonType.Ice,
        ["chopleberry"] = Pokemon.PokemonType.Fighting,
        ["kebiaberry"] = Pokemon.PokemonType.Poison,
        ["shucaberry"] = Pokemon.PokemonType.Ground,
        ["cobaberry"] = Pokemon.PokemonType.Flying,
        ["payapaberry"] = Pokemon.PokemonType.Psychic,
        ["tangaberry"] = Pokemon.PokemonType.Bug,
        ["chartiberry"] = Pokemon.PokemonType.Rock,
        ["kasibberry"] = Pokemon.PokemonType.Ghost,
        ["habanberry"] = Pokemon.PokemonType.Dragon,
        ["colburberry"] = Pokemon.PokemonType.Dark,
        ["babiriberry"] = Pokemon.PokemonType.Steel,
        ["roseliberry"] = Pokemon.PokemonType.Fairy,
    };

    /// <summary>
    /// Berries the holder never eats itself (Showdown's onEat: false): only Natural Gift, Fling and
    /// Bug Bite / Pluck use them.
    /// </summary>
    private static readonly string[] Inedible =
    [
        "razzberry", "blukberry", "nanabberry", "wepearberry", "pinapberry", "pomegberry", "kelpsyberry", "qualotberry",
        "hondewberry", "grepaberry", "tamatoberry", "cornnberry", "magostberry", "rabutaberry", "nomelberry", "spelonberry",
        "pamtreberry", "watmelberry", "durinberry", "belueberry",
    ];

    private static readonly HashSet<string> AllBerries =
    [
        Cheri, Chesto, Pecha, Rawst, Aspear, Leppa, Oran, Persim, Lum, Sitrus, Chilan, Liechi, Ganlon, Salac, Petaya,
        Apicot, Lansat, Starf, Enigma, Micle, Custap, Jaboca, Rowap, Kee, Maranga,
        .. FlavorHealers, .. TypeResist.Keys, .. Inedible,
    ];

    /// <summary>Every Gen 6 berry id.</summary>
    public static IReadOnlyCollection<string> All => AllBerries;

    public static bool IsBerry(string? item) => item is not null && AllBerries.Contains(item);
}
