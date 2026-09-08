using System.Text.Json;

using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class TalentCatalogStore(IHostEnvironment environment)
{
    private const string CatalogFileName = "talente_mit_spezialisierungen.json";

    private readonly Lazy<TalentCatalogSnapshot> _snapshot =
        new(() => LoadSnapshot(ResolveCatalogPath(environment)));

    public IEnumerable<TalentCatalogEntry> Entries => _snapshot.Value.Entries.Values;

    public TalentSpecializationRules SpecializationRules => _snapshot.Value.SpecializationRules;

    public bool TryGetEntry(string talentName, out TalentCatalogEntry entry)
    {
        return TalentCatalogText.TryFindBestNameMatch(
            _snapshot.Value.Entries.Values,
            static existingEntry => existingEntry.Name,
            talentName,
            out entry!);
    }

    private static string ResolveCatalogPath(IHostEnvironment environment)
    {
        var candidatePaths = new[]
        {
            Path.Combine(environment.ContentRootPath, "Data", CatalogFileName),
            Path.Combine(AppContext.BaseDirectory, "Data", CatalogFileName)
        };

        return candidatePaths.FirstOrDefault(File.Exists) ??
               throw new FileNotFoundException(
                   $"Der Talentkatalog '{CatalogFileName}' wurde nicht gefunden. Erwartete Pfade: " +
                   string.Join("; ", candidatePaths),
                   candidatePaths[0]);
    }

    private static TalentCatalogSnapshot LoadSnapshot(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var items = CatalogJsonValue.ReadArray(document.RootElement, "Talente")
                .Concat(CatalogJsonValue.ReadArray(document.RootElement, "SprachenUndSchriften"))
                .ToArray();
            if (items.Length == 0)
            {
                throw new InvalidDataException(
                    $"Der Talentkatalog '{path}' enthält keine Talenteinträge.");
            }

            var entries = items
                .Select(MapItem)
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .GroupBy(entry => TalentCatalogText.CanonicalizeName(entry.Name), StringComparer.Ordinal)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            return new TalentCatalogSnapshot(
                entries,
                MapSpecializationRules(document.RootElement));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Der Talentkatalog '{path}' ist kein gültiges JSON im erwarteten Wurzelformat.",
                exception);
        }
    }

    private static TalentCatalogEntry MapItem(JsonElement item)
    {
        var probes = CatalogJsonValue.ReadArray(item, "Probe")
            .Select(probe => probe.ValueKind == JsonValueKind.Array
                ? string.Join("/", probe.EnumerateArray().Select(CatalogJsonValue.ReadText))
                : CatalogJsonValue.ReadText(probe))
            .Select(TalentCatalogText.NormalizeProbe)
            .Where(probe => !string.IsNullOrWhiteSpace(probe))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var ruleHints = CatalogJsonValue.ReadArray(item, "RegelhinweiseKurz")
            .Select(CatalogJsonValue.ReadText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        var specializations = CatalogJsonValue.ReadArray(item, "Spezialisierungen")
            .Select(CatalogJsonValue.ReadText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        var specializationPrerequisites = ReadOptionalValue(item, "SpezialisierungsVoraussetzungen");
        var specializationHint = CatalogJsonValue.ReadText(item, "SpezialisierungsHinweis");
        var specializationText = BuildSpecializationText(
            specializations,
            CatalogJsonValue.ReadBool(item, "SpezialisierungenOffen") ?? false,
            specializationHint,
            specializationPrerequisites ?? string.Empty);

        return new TalentCatalogEntry(
            CatalogJsonValue.ReadText(item, "Name"),
            probes.Length == 1 ? probes[0] : string.Empty,
            probes,
            string.Equals(
                CatalogJsonValue.ReadText(item, "Typ"),
                "Basis",
                StringComparison.OrdinalIgnoreCase),
            CatalogJsonValue.ReadText(item, "Kategorie"),
            CatalogJsonValue.ReadText(item, "Kurzbeschreibung"),
            CatalogJsonValue.ReadText(item, "EffektiveBehinderung"),
            CatalogJsonValue.ReadText(item, "Voraussetzung"),
            ruleHints,
            specializations,
            CatalogJsonValue.ReadBool(item, "SpezialisierungenMoeglich") ?? false,
            CatalogJsonValue.ReadBool(item, "SpezialisierungenOffen") ?? false,
            specializationHint,
            specializationPrerequisites,
            BuildInfoSections(item, probes, specializationText));
    }

    private static TalentSpecializationRules MapSpecializationRules(JsonElement root)
    {
        if (!CatalogJsonValue.TryGetProperty(
                root,
                "Regelbasis_Talentspezialisierungen",
                out var ruleValue) ||
            ruleValue.ValueKind != JsonValueKind.Object)
        {
            return TalentSpecializationRules.Empty;
        }

        var thresholds = new Dictionary<int, int>();
        if (CatalogJsonValue.TryGetProperty(ruleValue, "MindestTaW", out var thresholdValue) &&
            thresholdValue.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in thresholdValue.EnumerateObject())
            {
                if (int.TryParse(property.Name, out var specializationNumber) &&
                    CatalogJsonValue.ReadInt(property.Value) is { } minimumTalentValue)
                {
                    thresholds[specializationNumber] = minimumTalentValue;
                }
            }
        }

        return new TalentSpecializationRules(
            CatalogJsonValue.ReadText(ruleValue, "Effekt"),
            thresholds,
            CatalogJsonValue.ReadBool(ruleValue, "DoppelteGleicheSpezialisierung") ?? false,
            CatalogJsonValue.ReadText(ruleValue, "Hinweis"));
    }

    private static ProbeInfoSectionDto[] BuildInfoSections(
        JsonElement item,
        IReadOnlyList<string> probes,
        string specializationText)
    {
        var sections = new List<ProbeInfoSectionDto>(capacity: 8);
        AddInfoSection(sections, "Beschreibung", CatalogJsonValue.ReadText(item, "Kurzbeschreibung"));
        AddInfoSection(sections, "Kategorie", CatalogJsonValue.ReadText(item, "Kategorie"));
        AddInfoSection(sections, "Probe", string.Join(" oder ", probes));
        AddInfoSection(sections, "Effektive Behinderung", CatalogJsonValue.ReadText(item, "EffektiveBehinderung"));
        AddInfoSection(sections, "Voraussetzung", CatalogJsonValue.ReadText(item, "Voraussetzung"));
        AddInfoSection(
            sections,
            "Regelhinweise",
            string.Join(Environment.NewLine, CatalogJsonValue.ReadArray(item, "RegelhinweiseKurz")
                .Select(CatalogJsonValue.ReadText)
                .Where(text => !string.IsNullOrWhiteSpace(text))));
        AddInfoSection(sections, "Spezialisierungen", specializationText);
        return sections.ToArray();
    }

    private static string BuildSpecializationText(
        IReadOnlyList<string> specializations,
        bool isOpen,
        string hint,
        string prerequisites)
    {
        var parts = new List<string>();
        if (specializations.Count > 0)
        {
            parts.Add(string.Join(
                Environment.NewLine,
                specializations.Select(specialization => $"- {specialization}")));
        }

        if (isOpen)
        {
            parts.Add("Weitere Spezialisierungen möglich.");
        }

        if (!isOpen && !string.IsNullOrWhiteSpace(hint))
        {
            parts.Add($"Hinweis: {hint}");
        }

        if (!string.IsNullOrWhiteSpace(prerequisites))
        {
            parts.Add($"Voraussetzungen: {prerequisites}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string? ReadOptionalValue(JsonElement item, string propertyName)
    {
        if (!CatalogJsonValue.TryGetProperty(item, propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var text = CatalogJsonValue.FormatValue(value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void AddInfoSection(
        ICollection<ProbeInfoSectionDto> sections,
        string label,
        string? value)
    {
        var normalizedValue = CatalogJsonValue.NormalizeDisplayText(value);
        if (!string.IsNullOrWhiteSpace(normalizedValue))
        {
            sections.Add(new ProbeInfoSectionDto(label, normalizedValue));
        }
    }

    private sealed record TalentCatalogSnapshot(
        IReadOnlyDictionary<string, TalentCatalogEntry> Entries,
        TalentSpecializationRules SpecializationRules);
}

public sealed record TalentCatalogEntry(
    string Name,
    string Probe,
    IReadOnlyList<string> ProbeAlternatives,
    bool IsBasisTalent,
    string Category,
    string ShortDescription,
    string EffectiveEncumbrance,
    string Prerequisite,
    IReadOnlyList<string> RuleHints,
    IReadOnlyList<string> Specializations,
    bool SpecializationsPossible,
    bool SpecializationsOpen,
    string SpecializationHint,
    string? SpecializationPrerequisites,
    IReadOnlyList<ProbeInfoSectionDto> InfoSections)
{
    public IReadOnlyList<string> AlternativeNames { get; } = Array.Empty<string>();

    public bool TryGetProbe(string? requestedProbe, out string probe)
    {
        var requestedCanonical = CanonicalizeProbe(requestedProbe);
        if (string.IsNullOrWhiteSpace(requestedCanonical))
        {
            probe = string.Empty;
            return false;
        }

        probe = ProbeAlternatives.FirstOrDefault(candidate =>
            string.Equals(CanonicalizeProbe(candidate), requestedCanonical, StringComparison.Ordinal)) ??
            string.Empty;
        return !string.IsNullOrWhiteSpace(probe);
    }

    private static string CanonicalizeProbe(string? probe)
    {
        return TalentCatalogText.CanonicalizeText(TalentCatalogText.NormalizeProbe(probe));
    }
}

public sealed record TalentSpecializationRules(
    string Effect,
    IReadOnlyDictionary<int, int> MinimumTalentValues,
    bool DuplicateSpecializationAllowed,
    string Note)
{
    public static TalentSpecializationRules Empty { get; } = new(
        string.Empty,
        new Dictionary<int, int>(),
        false,
        string.Empty);

    public bool TryGetAvailableSpecialization(
        TalentData talent,
        string? requestedSpecialization,
        out string? matchedSpecialization)
    {
        ArgumentNullException.ThrowIfNull(talent);

        var requestedCanonical = TalentCatalogText.CanonicalizeText(requestedSpecialization);
        if (string.IsNullOrWhiteSpace(requestedCanonical))
        {
            matchedSpecialization = null;
            return false;
        }

        var specializations = talent.Specializations
            .Where(specialization => !string.IsNullOrWhiteSpace(specialization))
            .DistinctBy(TalentCatalogText.CanonicalizeText, StringComparer.Ordinal)
            .ToArray();
        var specializationIndex = Array.FindIndex(
            specializations,
            specialization => string.Equals(
                TalentCatalogText.CanonicalizeText(specialization),
                requestedCanonical,
                StringComparison.Ordinal));

        if (specializationIndex < 0 ||
            !MinimumTalentValues.TryGetValue(specializationIndex + 1, out var minimumTalentValue) ||
            talent.Wert < minimumTalentValue)
        {
            matchedSpecialization = null;
            return false;
        }

        matchedSpecialization = specializations[specializationIndex];
        return true;
    }
}
