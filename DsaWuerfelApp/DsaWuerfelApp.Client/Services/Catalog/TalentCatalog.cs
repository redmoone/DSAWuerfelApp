using System.Net.Http.Json;
using System.Text.Json.Serialization;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed record TalentCatalogEntry(
    string Name,
    string Probe,
    bool IsBasisTalent,
    IReadOnlyList<string> AlternativeNames);

public static class TalentCatalog
{
    private static readonly object SyncRoot = new();

    private static IReadOnlyDictionary<string, TalentCatalogEntry> _entriesByCanonical =
        new Dictionary<string, TalentCatalogEntry>(StringComparer.Ordinal);

    private static Task? _loadTask;

    public static IReadOnlyDictionary<string, TalentCatalogEntry> EntriesByCanonical => _entriesByCanonical;

    public static Task EnsureLoadedAsync(HttpClient http)
    {
        if (_entriesByCanonical.Count > 0)
        {
            return Task.CompletedTask;
        }

        lock (SyncRoot)
        {
            _loadTask ??= LoadAsync(http);
            return _loadTask;
        }
    }

    public static bool TryGetEntry(string talentName, out TalentCatalogEntry entry)
    {
        return _entriesByCanonical.TryGetValue(TalentCatalogText.CanonicalizeName(talentName), out entry!);
    }

    private static async Task LoadAsync(HttpClient http)
    {
        try
        {
            var items =
                await http.GetFromJsonAsync<List<TalentCatalogItem>>("data/talente_mit_spezialisierungen.json") ?? [];

            _entriesByCanonical = items
                .Select(MapItem)
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .GroupBy(entry => TalentCatalogText.CanonicalizeName(entry.Name), StringComparer.Ordinal)
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
        return new TalentCatalogEntry(
            TalentCatalogText.NormalizeCatalogText(item.Name),
            TalentCatalogText.NormalizeProbe(item.Eigenschaften),
            string.Equals(
                TalentCatalogText.NormalizeCatalogText(item.Typ),
                "Basis",
                StringComparison.OrdinalIgnoreCase),
            TalentCatalogText.ParseAlternatives(item.Ersatz));
    }

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