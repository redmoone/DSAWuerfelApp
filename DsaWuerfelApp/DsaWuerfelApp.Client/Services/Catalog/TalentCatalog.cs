using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DsaWuerfelApp.Client.Services;

public sealed record TalentCatalogEntry(
    string Name,
    string Probe,
    bool IsBasisTalent,
    IReadOnlyList<string> AlternativeNames);

public static partial class TalentCatalog
{
    private static readonly object SyncRoot = new();

    private static IReadOnlyDictionary<string, TalentCatalogEntry> _entriesByCanonical =
        new Dictionary<string, TalentCatalogEntry>(StringComparer.Ordinal);

    private static Task? _loadTask;

    public static IReadOnlyDictionary<string, TalentCatalogEntry> EntriesByCanonical => _entriesByCanonical;

    public static async Task EnsureLoadedAsync(HttpClient http)
    {
        if (_entriesByCanonical.Count > 0)
        {
            return;
        }

        Task loadTask;
        lock (SyncRoot)
        {
            _loadTask ??= LoadAsync(http);
            loadTask = _loadTask;
        }

        await loadTask;
    }

    public static bool TryGetEntry(string talentName, out TalentCatalogEntry entry)
    {
        return _entriesByCanonical.TryGetValue(CanonicalizeName(talentName), out entry!);
    }

    public static string CanonicalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeCatalogText(RemoveSpecializationSuffix(value));
        normalized = WordBoundaryUndPattern().Replace(normalized, "/");

        var decomposed = normalized.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    public static string NormalizeCatalogText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim()
            .Replace("Ã¤", "ä", StringComparison.Ordinal)
            .Replace("Ã„", "Ä", StringComparison.Ordinal)
            .Replace("Ã¶", "ö", StringComparison.Ordinal)
            .Replace("Ã–", "Ö", StringComparison.Ordinal)
            .Replace("Ã¼", "ü", StringComparison.Ordinal)
            .Replace("Ãœ", "Ü", StringComparison.Ordinal)
            .Replace("ÃŸ", "ß", StringComparison.Ordinal)
            .Replace("Ã©", "é", StringComparison.Ordinal)
            .Replace("Ã¨", "è", StringComparison.Ordinal)
            .Replace("Ã¡", "á", StringComparison.Ordinal)
            .Replace("Ã³", "ó", StringComparison.Ordinal)
            .Replace("â€“", "-", StringComparison.Ordinal)
            .Replace("â€”", "-", StringComparison.Ordinal)
            .Replace("â€ž", "\"", StringComparison.Ordinal)
            .Replace("â€œ", "\"", StringComparison.Ordinal)
            .Replace("â€™", "'", StringComparison.Ordinal);

        if (normalized.Contains('�'))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(normalized)).Trim();
                if (!decoded.Contains('�'))
                {
                    normalized = decoded;
                }
            }
            catch
            {
            }
        }

        return normalized;
    }

    public static string RemoveSpecializationSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeCatalogText(value);
        var parenthesisIndex = normalized.IndexOf('(');
        return parenthesisIndex >= 0
            ? normalized[..parenthesisIndex].Trim()
            : normalized;
    }

    private static async Task LoadAsync(HttpClient http)
    {
        try
        {
            var items =
                await http.GetFromJsonAsync<List<TalentCatalogItem>>("data/talente_mit_spezialisierungen.json") ??
                [];

            _entriesByCanonical = items
                .Select(MapItem)
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .GroupBy(entry => CanonicalizeName(entry.Name), StringComparer.Ordinal)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.Ordinal);
        }
        catch
        {
            _entriesByCanonical = new Dictionary<string, TalentCatalogEntry>(StringComparer.Ordinal);
        }
    }

    private static TalentCatalogEntry MapItem(TalentCatalogItem item)
    {
        var name = NormalizeCatalogText(item.Name);
        var probe = NormalizeProbe(item.Eigenschaften);
        var isBasisTalent = string.Equals(NormalizeCatalogText(item.Typ), "Basis", StringComparison.OrdinalIgnoreCase);
        var alternatives = ParseAlternatives(item.Ersatz);

        return new TalentCatalogEntry(name, probe, isBasisTalent, alternatives);
    }

    private static string NormalizeProbe(string? value)
    {
        var normalized = NormalizeCatalogText(value);
        return normalized
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static IReadOnlyList<string> ParseAlternatives(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(RemoveSpecializationSuffix)
            .Select(NormalizeCatalogText)
            .Where(alternative => !string.IsNullOrWhiteSpace(alternative))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    [GeneratedRegex(@"\bund\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WordBoundaryUndPattern();

    private sealed class TalentCatalogItem
    {
        public string Name { get; set; } = string.Empty;
        public string Eigenschaften { get; set; } = string.Empty;
        public string Typ { get; set; } = string.Empty;
        public string Ersatz { get; set; } = string.Empty;

        [JsonPropertyName("Spezialisierungen")]
        public string Specializations { get; set; } = string.Empty;
    }
}