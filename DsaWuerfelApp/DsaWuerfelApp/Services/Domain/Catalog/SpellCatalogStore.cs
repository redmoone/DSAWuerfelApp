using System.Text.Json;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class SpellCatalogStore(IHostEnvironment environment)
{
    private const string CatalogFileName = "Zauber.json";

    private readonly Lazy<IReadOnlyDictionary<string, SpellCatalogEntry>> _entriesByCanonical =
        new(() => LoadEntries(ResolveCatalogPath(environment)));

    public IEnumerable<SpellCatalogEntry> Entries => _entriesByCanonical.Value.Values;

    public bool TryGetEntry(string spellName, out SpellCatalogEntry entry)
    {
        return TalentCatalogText.TryFindBestNameMatch(
            _entriesByCanonical.Value.Values,
            static existingEntry => existingEntry.Name,
            spellName,
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
                   $"Der Zauberkatalog '{CatalogFileName}' wurde nicht gefunden. Erwartete Pfade: " +
                   string.Join("; ", candidatePaths),
                   candidatePaths[0]);
    }

    private static IReadOnlyDictionary<string, SpellCatalogEntry> LoadEntries(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var spellItems = CatalogJsonValue.ReadArray(document.RootElement, "Zauber");
            if (spellItems.Count == 0)
            {
                throw new InvalidDataException(
                    $"Der Zauberkatalog '{path}' enthält keine Zaubereinträge.");
            }

            return spellItems
                .Select(MapItem)
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .GroupBy(entry => TalentCatalogText.CanonicalizeName(entry.Name), StringComparer.Ordinal)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Der Zauberkatalog '{path}' ist kein gültiges JSON im erwarteten Wurzelformat.",
                exception);
        }
    }

    private static SpellCatalogEntry MapItem(JsonElement item)
    {
        var name = CatalogJsonValue.ReadText(item, "Zauber");
        var probeDefinition = MapProbeDefinition(item);
        var modifications = CatalogJsonValue.ReadArray(item, "Modifikationen")
            .SelectMany(MapOption)
            .Where(option => !string.IsNullOrWhiteSpace(option.Name))
            .ToArray();
        var variants = CatalogJsonValue.ReadArray(item, "Varianten")
            .SelectMany(MapOption)
            .Where(option => !string.IsNullOrWhiteSpace(option.Name))
            .ToArray();

        return new SpellCatalogEntry(
            name,
            probeDefinition.MachineProbe,
            modifications,
            variants,
            BuildInfoSections(item, probeDefinition),
            BuildRuleSections(item),
            probeDefinition,
            MapStructuredValue(item, "ZauberdauerStrukturiert"),
            MapStructuredValue(item, "KostenStrukturiert"),
            MapStructuredValue(item, "ZielobjektStrukturiert"),
            MapStructuredValue(item, "ReichweiteStrukturiert"),
            MapStructuredValue(item, "WirkungsdauerStrukturiert"));
    }

    private static IReadOnlyList<SpellOptionEntry> MapOption(JsonElement item)
    {
        var name = CatalogJsonValue.ReadText(item, "Bezeichnung");
        var branchNames = GetStructuredOptionBranches(item).ToArray();

        return branchNames.Length == 0
            ? [MapOption(item, name, null)]
            : branchNames.Select(branch => MapOption(item, $"{name}: {branch}", branch)).ToArray();
    }

    private static SpellOptionEntry MapOption(JsonElement item, string name, string? branchName)
    {
        var requirementText = ReadOptionalValue(item, "Voraussetzung");
        var requirement = MapRequirement(item, requirementText);
        var probeModifier = MapOptionValue(item, "Probenmodifikator", branchName);
        var preRollZfp = MapOptionValue(item, "VorabZfP", branchName);
        var costChange = MapOptionValue(item, "Kosten\u00e4nderung", branchName);
        var castingTimeChange = MapOptionValue(item, "Zauberdauer\u00e4nderung", branchName);
        var durationChange = MapOptionValue(item, "Wirkungsdauer\u00e4nderung", branchName);

        var displayParts = new[]
        {
            BuildLabeledValue("Unterfall", branchName),
            CatalogJsonValue.ReadText(item, "Regel"),
            CatalogJsonValue.ReadText(item, "Wirkung"),
            BuildLabeledValue("Voraussetzung", requirementText),
            BuildLabeledValue("Probenmodifikator", probeModifier.DisplayText),
            BuildLabeledValue("Vorab-ZfP", preRollZfp.DisplayText),
            BuildLabeledValue("Kosten\u00e4nderung", costChange.DisplayText),
            BuildLabeledValue("Zauberdauer\u00e4nderung", castingTimeChange.DisplayText),
            BuildLabeledValue("Wirkungsdauer\u00e4nderung", durationChange.DisplayText)
        }.Where(text => !string.IsNullOrWhiteSpace(text));

        return new SpellOptionEntry(
            name,
            name,
            string.Join(Environment.NewLine, displayParts),
            requirement,
            probeModifier,
            preRollZfp,
            costChange,
            castingTimeChange,
            durationChange,
            !requirement.RequiresManualCheck);
    }

    private static IEnumerable<string> GetStructuredOptionBranches(JsonElement item)
    {
        var branches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propertyName in new[] { "Probenmodifikator", "VorabZfP" })
        {
            if (!CatalogJsonValue.TryGetProperty(item, propertyName, out var value) ||
                value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in value.EnumerateObject())
            {
                var branch = CatalogJsonValue.NormalizeDisplayText(property.Name);
                if (!string.IsNullOrWhiteSpace(branch))
                {
                    branches.Add(branch);
                }
            }
        }

        return branches.OrderBy(branch => branch, StringComparer.Ordinal);
    }
    private static SpellOptionRequirement MapRequirement(JsonElement item, string? requirementText)
    {
        if (!CatalogJsonValue.TryGetProperty(item, "Voraussetzung", out var requirementValue) ||
            requirementValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return SpellOptionRequirement.Empty;
        }

        if (requirementValue.ValueKind != JsonValueKind.Object)
        {
            return new SpellOptionRequirement(
                false,
                false,
                0,
                [],
                SpellRepresentationRestriction.Empty,
                [],
                requirementText ?? string.Empty,
                true);
        }

        var minimumSpellValue = CatalogJsonValue.ReadInt(requirementValue, "MindestZfW") ?? 0;
        var allowedRepresentations = MapRepresentation(
            requirementValue,
            "Repräsentation",
            out var hasUnresolvedAllowedRepresentation);
        var disallowedRepresentations = MapRepresentation(
            requirementValue,
            "NichtRepräsentation",
            out var hasUnresolvedDisallowedRepresentation);

        var recognizedProperties = new[]
        {
            CatalogJsonValue.CanonicalizePropertyName("MindestZfW"),
            CatalogJsonValue.CanonicalizePropertyName("Repräsentation"),
            CatalogJsonValue.CanonicalizePropertyName("NichtRepräsentation")
        }.ToHashSet(StringComparer.Ordinal);
        var hasUnknownProperty = requirementValue.EnumerateObject()
            .Any(property => !recognizedProperties.Contains(
                CatalogJsonValue.CanonicalizePropertyName(property.Name)));

        return new SpellOptionRequirement(
            false,
            false,
            minimumSpellValue,
            [],
            new SpellRepresentationRestriction(
                allowedRepresentations,
                disallowedRepresentations),
            [],
            requirementText ?? string.Empty,
            hasUnknownProperty ||
            hasUnresolvedAllowedRepresentation ||
            hasUnresolvedDisallowedRepresentation);
    }

    private static string[] MapRepresentation(
        JsonElement requirement,
        string propertyName,
        out bool unresolved)
    {
        unresolved = false;
        if (!CatalogJsonValue.TryGetProperty(requirement, propertyName, out var value))
        {
            return [];
        }

        var text = CatalogJsonValue.ReadText(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            unresolved = true;
            return [];
        }

        var canonicalText = CatalogJsonValue.CanonicalizePropertyName(text);
        if (canonicalText.Contains("und", StringComparison.Ordinal) ||
            canonicalText.Contains(",", StringComparison.Ordinal))
        {
            unresolved = true;
            return [];
        }

        var representation = SpellRepresentationText.Canonicalize(text);
        if (string.IsNullOrWhiteSpace(representation))
        {
            unresolved = true;
            return [];
        }

        return [representation];
    }

    private static SpellOptionValue MapOptionValue(JsonElement item, string propertyName, string? branchName = null)
    {
        if (!CatalogJsonValue.TryGetProperty(item, propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return SpellOptionValue.Empty;
        }

        if (!string.IsNullOrWhiteSpace(branchName) && value.ValueKind == JsonValueKind.Object)
        {
            if (!CatalogJsonValue.TryGetProperty(value, branchName, out value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return SpellOptionValue.Empty;
            }
        }

        return new SpellOptionValue(
            CatalogJsonValue.FormatValue(value),
            CatalogJsonValue.ReadInt(value),
            value.ValueKind == JsonValueKind.Number ||
            value.ValueKind == JsonValueKind.String && CatalogJsonValue.ReadInt(value).HasValue,
            true);
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

    private static SpellProbeDefinition MapProbeDefinition(JsonElement item)
    {
        var rawText = CatalogJsonValue.ReadText(item, "Probe");
        var structuredAttributes = CatalogJsonValue.TryGetProperty(
                item,
                "ProbeStrukturiert",
                out var structuredProbeValue)
            ? CatalogJsonValue.ReadArray(structuredProbeValue, "Eigenschaften")
                .Select(CatalogJsonValue.ReadText)
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute))
                .ToArray()
            : [];
        var machineProbe = structuredAttributes.Length == 3
            ? string.Join("/", structuredAttributes)
            : NormalizeMachineProbe(rawText);

        var structuredProbe = structuredProbeValue;

        return new SpellProbeDefinition(
            rawText,
            machineProbe,
            structuredAttributes,
            CatalogJsonValue.ReadText(structuredProbe, "DynamischeEigenschaft"),
            CatalogJsonValue.ReadBool(structuredProbe, "GegenMR") ?? false,
            CatalogJsonValue.ReadArray(structuredProbe, "WeitereModifikatoren")
                .Select(CatalogJsonValue.ReadText)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray());
    }

    private static string NormalizeMachineProbe(string value)
    {
        var withoutModifiers = value.Split('(', 2, StringSplitOptions.TrimEntries)[0];
        return TalentCatalogText.NormalizeProbe(withoutModifiers);
    }

    private static SpellStructuredValue? MapStructuredValue(JsonElement item, string propertyName)
    {
        if (!CatalogJsonValue.TryGetProperty(item, propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var values = value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject()
                .ToDictionary(
                    property => CatalogJsonValue.NormalizeDisplayText(property.Name),
                    property => CatalogJsonValue.FormatValue(property.Value),
                    StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        return new SpellStructuredValue(
            CatalogJsonValue.ReadText(value, "Typ"),
            CatalogJsonValue.ReadText(value, "Rohtext"),
            values);
    }

    private static ProbeInfoSectionDto[] BuildInfoSections(
        JsonElement item,
        SpellProbeDefinition probeDefinition)
    {
        var sections = new List<ProbeInfoSectionDto>(capacity: 10);
        AddInfoSection(sections, "Probe", probeDefinition.RawText);
        AddInfoSection(sections, "Zauberdauer", CatalogJsonValue.ReadText(item, "Zauberdauer"));
        AddInfoSection(sections, "Wirkung", CatalogJsonValue.ReadText(item, "Wirkung"));
        AddInfoSection(sections, "Kosten", CatalogJsonValue.ReadText(item, "Kosten"));
        AddInfoSection(sections, "Zielobjekt", CatalogJsonValue.ReadText(item, "Zielobjekt"));
        AddInfoSection(sections, "Reichweite", CatalogJsonValue.ReadText(item, "Reichweite"));
        AddInfoSection(sections, "Wirkungsdauer", CatalogJsonValue.ReadText(item, "Wirkungsdauer"));
        AddInfoSection(sections, "Reversalis", CatalogJsonValue.ReadText(item, "Reversalis"));
        AddInfoSection(sections, "Antimagie", CatalogJsonValue.ReadText(item, "Antimagie"));
        return sections.ToArray();
    }

    private static SpellInfoRuleSection[] BuildRuleSections(JsonElement item)
    {
        var sections = new List<SpellInfoRuleSection>(capacity: 7);
        AddRuleSection(
            sections,
            "Sonderregeln",
            CatalogJsonValue.ReadArray(item, "Sonderregeln"),
            FormatSpecialRule);
        AddRuleSection(
            sections,
            "Folgeproben",
            CatalogJsonValue.ReadArray(item, "Folgeproben"),
            value => FormatRule(value, "Akteur", "Probe", "Modifikator", "BeiMisslingen", "Quelle", "Regelsatz",
                "AutomatischAuswertbar"));
        AddRuleSection(
            sections,
            "Schaden",
            CatalogJsonValue.ReadArray(item, "Schaden"),
            value => FormatRule(value, "Ausdruck", "Art", "Quelle", "QuelleText", "AutomatischAuswertbar"));
        AddRuleSection(
            sections,
            "ZfP*-Schwellen",
            CatalogJsonValue.ReadArray(item, "ZfP_Schwellen"),
            value => FormatRule(value, "Schwelle", "Kontext", "Quelle"));
        AddRuleSection(
            sections,
            "Wertänderungen",
            CatalogJsonValue.ReadArray(item, "Wertänderungen"),
            value => CatalogJsonValue.FormatValue(value));
        AddRuleSection(
            sections,
            "Zauberprobenmodifikatoren",
            CatalogJsonValue.ReadArray(item, "ZauberprobenModifikatoren"),
            value => FormatRule(value, "Quelle", "Modifikator", "Regelsatz", "AutomatischAuswertbar"));
        AddRuleSection(
            sections,
            "Meisterentscheidungen",
            CatalogJsonValue.ReadArray(item, "Meisterentscheidungen"),
            value => FormatRule(value, "Quelle", "Regel"));
        return sections.ToArray();
    }

    private static void AddRuleSection(
        ICollection<SpellInfoRuleSection> sections,
        string label,
        IReadOnlyList<JsonElement> values,
        Func<JsonElement, string> formatter)
    {
        var entries = values
            .Select(formatter)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        if (entries.Length > 0)
        {
            sections.Add(new SpellInfoRuleSection(label, entries));
        }
    }

    private static string FormatSpecialRule(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return CatalogJsonValue.FormatValue(value);
        }

        return FormatRule(value, "Bezeichnung", "Regel", "Wirkung");
    }

    private static string FormatRule(JsonElement value, params string[] propertyNames)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return CatalogJsonValue.FormatValue(value);
        }

        var parts = propertyNames
            .Select(propertyName => BuildLabeledValue(
                propertyName,
                ReadOptionalValue(value, propertyName)))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        var automatic = CatalogJsonValue.ReadBool(value, "AutomatischAuswertbar");
        if (automatic.HasValue)
        {
            parts.Add(automatic.Value
                ? "Automatisch auswertbar: ja"
                : "Automatisch auswertbar: nein; manuell prüfen");
        }

        return string.Join("; ", parts);
    }

    private static string BuildLabeledValue(string label, string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label}: {value}";
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
}

public sealed record SpellCatalogEntry(
    string Name,
    string Probe,
    IReadOnlyList<SpellOptionEntry> Modifications,
    IReadOnlyList<SpellOptionEntry> Variants,
    IReadOnlyList<ProbeInfoSectionDto> InfoSections,
    IReadOnlyList<SpellInfoRuleSection> RuleSections,
    SpellProbeDefinition ProbeDefinition,
    SpellStructuredValue? CastingTimeStructured,
    SpellStructuredValue? CostStructured,
    SpellStructuredValue? TargetObjectStructured,
    SpellStructuredValue? RangeStructured,
    SpellStructuredValue? DurationStructured);

public sealed record SpellOptionEntry(
    string Name,
    string DisplayLabel,
    string DisplayText,
    SpellOptionRequirement Requirement,
    SpellOptionValue ProbeModifier,
    SpellOptionValue PreRollZfp,
    SpellOptionValue CostChange,
    SpellOptionValue CastingTimeChange,
    SpellOptionValue DurationChange,
    bool IsSelectionEnabled)
{
    public bool RequiresManualCalculation
    {
        get
        {
            if (ProbeModifier.IsPresent && !ProbeModifier.IsSimpleNumeric ||
                PreRollZfp.IsPresent && !PreRollZfp.IsSimpleNumeric)
            {
                return true;
            }

            if (!ProbeModifier.IsPresent && !PreRollZfp.IsPresent)
            {
                return !string.IsNullOrWhiteSpace(DisplayText);
            }

            return false;
        }
    }
}

public sealed record SpellOptionValue(
    string DisplayText,
    int? NumericValue,
    bool IsSimpleNumeric,
    bool IsPresent = false)
{
    public static SpellOptionValue Empty { get; } = new(string.Empty, null, false, false);
}

public sealed record SpellOptionRequirement(
    bool RequiresOwnRepresentation,
    bool AllowsForeignRepresentationWithMatrixUnderstanding,
    int MinimumSpellValue,
    IReadOnlyList<SpellOptionRequirementMode> Modes,
    SpellRepresentationRestriction RepresentationRestriction,
    IReadOnlyList<string> AdditionalRequirements,
    string RequirementText = "",
    bool RequiresManualCheck = false)
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

public sealed record SpellInfoRuleSection(
    string Label,
    IReadOnlyList<string> Entries);

public sealed record SpellProbeDefinition(
    string RawText,
    string MachineProbe,
    IReadOnlyList<string> Attributes,
    string DynamicAttribute,
    bool AgainstMagicResistance,
    IReadOnlyList<string> FurtherModifiers);

public sealed record SpellStructuredValue(
    string Type,
    string RawText,
    IReadOnlyDictionary<string, string> Values);
