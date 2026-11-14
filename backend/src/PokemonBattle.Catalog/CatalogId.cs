namespace PokemonBattle.Catalog;

/// <summary>Showdown-style identifiers: lowercase ASCII letters and digits only.</summary>
public static class CatalogId
{
    public static string From(string name) =>
        new(name.ToLowerInvariant().Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9')).ToArray());
}
