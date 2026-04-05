using System.Text.Json;
using System.Text.Json.Serialization;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class TalentCatalogStore(IHostEnvironment environment)
{
    private const string CatalogFileName = "talente_mit_spezialisierungen.json";

    private readonly Lazy<IReadOnlyDictionary<string, TalentCatalogEntry>> _entriesByCanonical =
        new(() => LoadEntries(ResolveCatalogPath(environment)));

    public IEnumerable<TalentCatalogEntry> Entries => _entriesByCanonical.Value.Values;

    public bool TryGetEntry(string talentName, out TalentCatalogEntry entry)
    {
        return _entriesByCanonical.Value.TryGetValue(TalentCatalogText.CanonicalizeName(talentName), out entry!);
    }

    private static string ResolveCatalogPath(IHostEnvironment environment)
    {
        var candidatePaths = new[]
        {
            Path.Combine(environment.ContentRootPath, "Data", CatalogFileName), Path.GetFullPath(Path.Combine(
                environment.ContentRootPath,
                "..",
                "DsaWuerfelApp.Client",
                "wwwroot",
                "data",
                CatalogFileName)),
            Path.Combine(AppContext.BaseDirectory, "Data", CatalogFileName)
        };

        return candidatePaths.FirstOrDefault(File.Exists) ?? candidatePaths[0];
    }

    private static IReadOnlyDictionary<string, TalentCatalogEntry> LoadEntries(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, TalentCatalogEntry>(StringComparer.Ordinal);
        }

        try
        {
            using var stream = File.OpenRead(path);
            var items = JsonSerializer.Deserialize<List<TalentCatalogItem>>(stream) ?? [];

            return items
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
            return new Dictionary<string, TalentCatalogEntry>(StringComparer.Ordinal);
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
            TalentCatalogText.ParseAlternatives(item.Ersatz),
            BuildInfoSections(item));
    }

    private static ProbeInfoSectionDto[] BuildInfoSections(TalentCatalogItem item)
    {
        var sections = new List<ProbeInfoSectionDto>(capacity: 5);
        AddInfoSection(sections, "Zweck", item.Purpose);
        AddInfoSection(sections, "Kernregel", item.CoreRule);
        AddInfoSection(sections, "Misslingen", item.Failure);
        AddInfoSection(sections, "Modifikatoren", item.Modifiers);
        AddInfoSection(sections, "Optional", item.OptionalNotes);
        return sections.ToArray();
    }

    private static void AddInfoSection(ICollection<ProbeInfoSectionDto> sections, string label, string? value)
    {
        var normalizedValue = TalentCatalogText.NormalizeCatalogText(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return;
        }

        sections.Add(new ProbeInfoSectionDto(label, normalizedValue));
    }

    private sealed class TalentCatalogItem
    {
        public string Name { get; set; } = string.Empty;
        public string Eigenschaften { get; set; } = string.Empty;
        public string Typ { get; set; } = string.Empty;
        public string Ersatz { get; set; } = string.Empty;

        [JsonPropertyName("Zweck")] public string Purpose { get; set; } = string.Empty;

        [JsonPropertyName("Kernregel")] public string CoreRule { get; set; } = string.Empty;

        [JsonPropertyName("Misslingen")] public string Failure { get; set; } = string.Empty;

        [JsonPropertyName("Modifikatoren")] public string Modifiers { get; set; } = string.Empty;

        [JsonPropertyName("Optional")] public string OptionalNotes { get; set; } = string.Empty;
    }
}

public sealed record TalentCatalogEntry(
    string Name,
    string Probe,
    bool IsBasisTalent,
    IReadOnlyList<string> AlternativeNames,
    IReadOnlyList<ProbeInfoSectionDto> InfoSections);