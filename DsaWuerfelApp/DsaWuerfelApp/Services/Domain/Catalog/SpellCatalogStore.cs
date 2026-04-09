using System.Text.Json;
using System.Text.Json.Serialization;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class SpellCatalogStore(IHostEnvironment environment)
{
    private const string CatalogFileName = "Zauber.json";

    private readonly Lazy<IReadOnlyDictionary<string, SpellCatalogEntry>> _entriesByCanonical =
        new(() => LoadEntries(ResolveCatalogPath(environment)));

    public bool TryGetEntry(string spellName, out SpellCatalogEntry entry)
    {
        return _entriesByCanonical.Value.TryGetValue(TalentCatalogText.CanonicalizeName(spellName), out entry!);
    }

    private static string ResolveCatalogPath(IHostEnvironment environment)
    {
        var candidatePaths = new[]
        {
            Path.Combine(environment.ContentRootPath, "Data", CatalogFileName), Path.GetFullPath(Path.Combine(
                environment.ContentRootPath, "..", "..", "..", "..", "Downloads",
                CatalogFileName)),
            Path.Combine(AppContext.BaseDirectory, "Data", CatalogFileName)
        };

        return candidatePaths.FirstOrDefault(File.Exists) ?? candidatePaths[0];
    }

    private static IReadOnlyDictionary<string, SpellCatalogEntry> LoadEntries(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, SpellCatalogEntry>(StringComparer.Ordinal);
        }

        try
        {
            using var stream = File.OpenRead(path);
            var catalog = JsonSerializer.Deserialize<SpellCatalogRoot>(stream) ?? new SpellCatalogRoot();

            return catalog.Spells
                .Select(MapItem)
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .GroupBy(entry => TalentCatalogText.CanonicalizeName(entry.Name), StringComparer.Ordinal)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, SpellCatalogEntry>(StringComparer.Ordinal);
        }
    }

    private static SpellCatalogEntry MapItem(SpellCatalogItem item)
    {
        var name = TalentCatalogText.NormalizeCatalogText(item.Name);
        var modifications = item.Modifications
            .Select(MapOption)
            .Where(option => !string.IsNullOrWhiteSpace(option.Name))
            .ToArray();
        var variants = item.Variants
            .Select(MapVariant)
            .Where(option => !string.IsNullOrWhiteSpace(option.Name))
            .ToArray();

        return new SpellCatalogEntry(
            name,
            TalentCatalogText.NormalizeProbe(item.Probe),
            modifications,
            variants,
            BuildInfoSections(item, modifications, variants));
    }

    private static SpellOptionEntry MapOption(SpellOptionItem item)
    {
        return new SpellOptionEntry(
            TalentCatalogText.NormalizeCatalogText(item.Label),
            TalentCatalogText.NormalizeCatalogText(item.Rule),
            null);
    }

    private static SpellOptionEntry MapVariant(SpellVariantItem item)
    {
        return new SpellOptionEntry(
            TalentCatalogText.NormalizeCatalogText(item.Label),
            TalentCatalogText.NormalizeCatalogText(item.Rule),
            TalentCatalogText.NormalizeCatalogText(item.Effect));
    }

    private static ProbeInfoSectionDto[] BuildInfoSections(
        SpellCatalogItem item,
        IReadOnlyList<SpellOptionEntry> modifications,
        IReadOnlyList<SpellOptionEntry> variants)
    {
        var sections = new List<ProbeInfoSectionDto>(capacity: 10);
        AddInfoSection(sections, "Zauberdauer", item.CastingTime);
        AddInfoSection(sections, "Wirkung", item.Effect);
        AddInfoSection(sections, "Kosten", item.Cost);
        AddInfoSection(sections, "Zielobjekt", item.TargetObject);
        AddInfoSection(sections, "Reichweite", item.Range);
        AddInfoSection(sections, "Wirkungsdauer", item.Duration);
        AddInfoSection(sections, "Modifikationen", BuildOptionSectionText(modifications));
        AddInfoSection(sections, "Varianten", BuildOptionSectionText(variants));
        AddInfoSection(sections, "Reversalis", item.Reversalis);
        AddInfoSection(sections, "Antimagie", item.AntiMagic);
        return sections.ToArray();
    }

    private static string BuildOptionSectionText(IEnumerable<SpellOptionEntry> options)
    {
        return string.Join(
            Environment.NewLine,
            options.Select(BuildOptionText).Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string BuildOptionText(SpellOptionEntry option)
    {
        var details = new[] { option.Rule, option.Effect }.Where(value => !string.IsNullOrWhiteSpace(value));

        var suffix = string.Join(" ", details);
        return string.IsNullOrWhiteSpace(suffix)
            ? option.Name
            : $"{option.Name}: {suffix}";
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

    private sealed class SpellCatalogRoot
    {
        [JsonPropertyName("Zauber")] public List<SpellCatalogItem> Spells { get; set; } = [];
    }

    private sealed class SpellCatalogItem
    {
        [JsonPropertyName("Zauber")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Probe")] public string Probe { get; set; } = string.Empty;
        [JsonPropertyName("Zauberdauer")] public string CastingTime { get; set; } = string.Empty;
        [JsonPropertyName("Wirkung")] public string Effect { get; set; } = string.Empty;
        [JsonPropertyName("Kosten")] public string Cost { get; set; } = string.Empty;
        [JsonPropertyName("Zielobjekt")] public string TargetObject { get; set; } = string.Empty;
        [JsonPropertyName("Reichweite")] public string Range { get; set; } = string.Empty;
        [JsonPropertyName("Wirkungsdauer")] public string Duration { get; set; } = string.Empty;
        [JsonPropertyName("Reversalis")] public string Reversalis { get; set; } = string.Empty;
        [JsonPropertyName("Antimagie")] public string AntiMagic { get; set; } = string.Empty;
        [JsonPropertyName("Modifikationen")] public List<SpellOptionItem> Modifications { get; set; } = [];
        [JsonPropertyName("Varianten")] public List<SpellVariantItem> Variants { get; set; } = [];
    }

    private class SpellOptionItem
    {
        [JsonPropertyName("Bezeichnung")] public string Label { get; set; } = string.Empty;
        [JsonPropertyName("Regel")] public string Rule { get; set; } = string.Empty;
    }

    private sealed class SpellVariantItem : SpellOptionItem
    {
        [JsonPropertyName("Wirkung")] public string Effect { get; set; } = string.Empty;
    }
}

public sealed record SpellCatalogEntry(
    string Name,
    string Probe,
    IReadOnlyList<SpellOptionEntry> Modifications,
    IReadOnlyList<SpellOptionEntry> Variants,
    IReadOnlyList<ProbeInfoSectionDto> InfoSections);

public sealed record SpellOptionEntry(string Name, string? Rule, string? Effect);