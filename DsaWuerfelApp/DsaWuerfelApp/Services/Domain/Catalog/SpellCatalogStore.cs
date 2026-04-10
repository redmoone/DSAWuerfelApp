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
            .Select(MapOption)
            .Where(option => !string.IsNullOrWhiteSpace(option.Name))
            .ToArray();

        return new SpellCatalogEntry(
            name,
            TalentCatalogText.NormalizeProbe(item.Probe),
            modifications,
            variants,
            BuildInfoSections(item));
    }

    private static SpellOptionEntry MapOption(SpellOptionItem item)
    {
        var name = TalentCatalogText.NormalizeCatalogText(item.Label);
        var displayLabel = NormalizeOptionalText(item.Heading) ?? name;
        var displayText = NormalizeOptionalText(item.ShortText) ??
                          BuildFallbackDisplayText(item.Rule, item.Effect) ??
                          displayLabel;

        return new SpellOptionEntry(
            name,
            displayLabel,
            displayText,
            MapRequirement(item.Visibility));
    }

    private static SpellOptionRequirement MapRequirement(SpellOptionRequirementItem? item)
    {
        if (item is null)
        {
            return SpellOptionRequirement.Empty;
        }

        var modes = item.Modes
            .Select(MapRequirementMode)
            .ToArray();

        return new SpellOptionRequirement(
            item.RequiresOwnRepresentation,
            item.AllowsForeignRepresentationWithMatrixUnderstanding,
            item.MinimumSpellValue ?? 0,
            modes,
            new SpellRepresentationRestriction(
                item.AllowedRepresentations
                    .Select(SpellRepresentationText.Canonicalize)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                item.DisallowedRepresentations
                    .Select(SpellRepresentationText.Canonicalize)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()),
            item.AdditionalRequirements
                .Select(TalentCatalogText.NormalizeCatalogText)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray());
    }

    private static SpellOptionRequirementMode MapRequirementMode(SpellOptionRequirementModeItem item)
    {
        return new SpellOptionRequirementMode(
            TalentCatalogText.NormalizeCatalogText(item.Name),
            item.MinimumSpellValue ?? 0);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = TalentCatalogText.NormalizeCatalogText(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? BuildFallbackDisplayText(string? rule, string? effect)
    {
        var parts = new[] { NormalizeOptionalText(rule), NormalizeOptionalText(effect) }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return parts.Length == 0 ? null : string.Join(" ", parts);
    }

    private static ProbeInfoSectionDto[] BuildInfoSections(SpellCatalogItem item)
    {
        var sections = new List<ProbeInfoSectionDto>(capacity: 8);
        AddInfoSection(sections, "Zauberdauer", item.CastingTime);
        AddInfoSection(sections, "Wirkung", item.Effect);
        AddInfoSection(sections, "Kosten", item.Cost);
        AddInfoSection(sections, "Zielobjekt", item.TargetObject);
        AddInfoSection(sections, "Reichweite", item.Range);
        AddInfoSection(sections, "Wirkungsdauer", item.Duration);
        AddInfoSection(sections, "Reversalis", item.Reversalis);
        AddInfoSection(sections, "Antimagie", item.AntiMagic);
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
        [JsonPropertyName("Varianten")] public List<SpellOptionItem> Variants { get; set; } = [];
    }

    private sealed class SpellOptionItem
    {
        [JsonPropertyName("Bezeichnung")] public string Label { get; set; } = string.Empty;
        [JsonPropertyName("Regel")] public string Rule { get; set; } = string.Empty;
        [JsonPropertyName("Wirkung")] public string Effect { get; set; } = string.Empty;
        [JsonPropertyName("Überschrift")] public string Heading { get; set; } = string.Empty;
        [JsonPropertyName("Kurztext")] public string ShortText { get; set; } = string.Empty;
        [JsonPropertyName("AnzeigenWenn")] public SpellOptionRequirementItem? Visibility { get; set; }
    }

    private sealed class SpellOptionRequirementItem
    {
        [JsonPropertyName("EigeneRepräsentation")]
        public bool RequiresOwnRepresentation { get; set; }

        [JsonPropertyName("MatrixverständnisErlaubt")]
        public bool AllowsForeignRepresentationWithMatrixUnderstanding { get; set; }

        [JsonPropertyName("MindestZfW")] public int? MinimumSpellValue { get; set; }

        [JsonPropertyName("Modi")] public List<SpellOptionRequirementModeItem> Modes { get; set; } = [];

        [JsonPropertyName("ErlaubteRepräsentationen")]
        public List<string> AllowedRepresentations { get; set; } = [];

        [JsonPropertyName("AusgeschlosseneRepräsentationen")]
        public List<string> DisallowedRepresentations { get; set; } = [];

        [JsonPropertyName("WeitereVoraussetzungen")]
        public List<string> AdditionalRequirements { get; set; } = [];
    }

    private sealed class SpellOptionRequirementModeItem
    {
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("MindestZfW")] public int? MinimumSpellValue { get; set; }
    }
}

public sealed record SpellCatalogEntry(
    string Name,
    string Probe,
    IReadOnlyList<SpellOptionEntry> Modifications,
    IReadOnlyList<SpellOptionEntry> Variants,
    IReadOnlyList<ProbeInfoSectionDto> InfoSections);

public sealed record SpellOptionEntry(
    string Name,
    string DisplayLabel,
    string DisplayText,
    SpellOptionRequirement Requirement);

public sealed record SpellOptionRequirement(
    bool RequiresOwnRepresentation,
    bool AllowsForeignRepresentationWithMatrixUnderstanding,
    int MinimumSpellValue,
    IReadOnlyList<SpellOptionRequirementMode> Modes,
    SpellRepresentationRestriction RepresentationRestriction,
    IReadOnlyList<string> AdditionalRequirements)
{
    public static SpellOptionRequirement Empty { get; } = new(
        false,
        false,
        0,
        Array.Empty<SpellOptionRequirementMode>(),
        SpellRepresentationRestriction.Empty,
        Array.Empty<string>());
}

public sealed record SpellOptionRequirementMode(
    string Name,
    int MinimumSpellValue);

public sealed record SpellRepresentationRestriction(
    IReadOnlyList<string> AllowedRepresentations,
    IReadOnlyList<string> DisallowedRepresentations)
{
    public static SpellRepresentationRestriction Empty { get; } =
        new(Array.Empty<string>(), Array.Empty<string>());
}
