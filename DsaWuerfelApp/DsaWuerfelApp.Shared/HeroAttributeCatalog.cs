namespace DsaWuerfelApp.Shared;

public static class HeroAttributeCatalog
{
    public const string Mut = "MU";
    public const string Klugheit = "KL";
    public const string Intuition = "IN";
    public const string Charisma = "CH";
    public const string Fingerfertigkeit = "FF";
    public const string Gewandtheit = "GE";
    public const string Konstitution = "KO";
    public const string Koerperkraft = "KK";

    private static readonly IReadOnlyDictionary<string, int> DefaultValuesMap = new Dictionary<string, int>
    {
        [Mut] = 14,
        [Klugheit] = 13,
        [Intuition] = 15,
        [Charisma] = 12,
        [Fingerfertigkeit] = 15,
        [Gewandtheit] = 15,
        [Konstitution] = 14,
        [Koerperkraft] = 13
    };

    private static readonly string[] OrderedAttributes =
    [
        Mut,
        Klugheit,
        Intuition,
        Charisma,
        Fingerfertigkeit,
        Gewandtheit,
        Konstitution,
        Koerperkraft
    ];

    public static IReadOnlyDictionary<string, int> DefaultValues => DefaultValuesMap;

    public static IReadOnlyList<string> Order => OrderedAttributes;

    public static bool TryGetDefaultValue(string attribute, out int value)
    {
        return DefaultValuesMap.TryGetValue(attribute, out value);
    }
}