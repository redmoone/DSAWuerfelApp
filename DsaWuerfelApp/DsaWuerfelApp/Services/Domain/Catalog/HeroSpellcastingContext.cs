using System.Text;
using System.Xml.Linq;

using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

internal sealed class HeroSpellcastingContext
{
    private static readonly TraditionDefinition[] TraditionDefinitions =
    [
        new("gildenmagier", "Tradition Gildenmagier", HeroAttributeCatalog.Klugheit, ["magier"], ["gildenmag"]),
        new("alchimist", "Tradition Alchimist", HeroAttributeCatalog.Klugheit, ["magier"], ["alchimist", "alchemist"]),
        new("borbaradianer", "Tradition Borbaradianer", HeroAttributeCatalog.Klugheit, ["borbaradian"], ["borbarad"]),
        new("druide", "Tradition Druide", HeroAttributeCatalog.Klugheit, ["druide"], ["druid"]),
        new("geode_herren_der_erde", "Tradition Geode (Herren der Erde)", HeroAttributeCatalog.Klugheit, ["geode"],
            ["herrendererde"]),
        new("scharlatan", "Tradition Scharlatan", HeroAttributeCatalog.Klugheit, ["scharlatan"], ["scharlat"]),
        new("zibilja", "Tradition Zibilja", HeroAttributeCatalog.Klugheit, ["zibilja"], ["zibilja", "zibil"]),
        new(
            "achaz_kristallomant",
            "Tradition Achaz-Kristallomant",
            HeroAttributeCatalog.Intuition,
            ["kristallomant"],
            ["achazkristallomant", "kristallomant"]),
        new("derwisch", "Tradition Derwisch", HeroAttributeCatalog.Intuition, ["derwisch"], ["derwisch"]),
        new("durro_dun", "Tradition Durro-Dun", HeroAttributeCatalog.Intuition, ["durrodun", "schamane"],
            ["durrodun"]),
        new("elf", "Tradition Elf", HeroAttributeCatalog.Intuition, ["elf"], ["elf"]),
        new(
            "ferkina_besessener",
            "Tradition Ferkina-Besessener",
            HeroAttributeCatalog.Intuition,
            ["ferkinabesessener", "schamane"],
            ["ferkinabesessen", "ferkinabesessener"]),
        new("geode_diener_sumus", "Tradition Geode (Diener Sumus)", HeroAttributeCatalog.Intuition, ["geode"],
            ["dienersumus"]),
        new("hexe", "Tradition Hexe", HeroAttributeCatalog.Intuition, ["hexe"], ["hexe", "hex"]),
        new("schamane", "Tradition Schamane", HeroAttributeCatalog.Intuition, ["schamane"], ["schaman"]),
        new("schelm", "Tradition Schelm", HeroAttributeCatalog.Intuition, ["schelm"], ["schelm"]),
        new("zaubertanzer", "Tradition Zaubertanzer", HeroAttributeCatalog.Intuition, ["zaubertanzer"],
            ["zaubertanz"])
    ];

    private static readonly IReadOnlyDictionary<string, LeadAttributeResolution> RepresentationLeadAttributes =
        new Dictionary<string, LeadAttributeResolution>(StringComparer.Ordinal)
        {
            ["magier"] = new(HeroAttributeCatalog.Klugheit, "Repräsentation Magier"),
            ["borbaradian"] = new(HeroAttributeCatalog.Klugheit, "Repräsentation Borbaradianer"),
            ["druide"] = new(HeroAttributeCatalog.Klugheit, "Repräsentation Druide"),
            ["scharlatan"] = new(HeroAttributeCatalog.Klugheit, "Repräsentation Scharlatan"),
            ["zibilja"] = new(HeroAttributeCatalog.Klugheit, "Repräsentation Zibilja"),
            ["derwisch"] = new(HeroAttributeCatalog.Intuition, "Repräsentation Derwisch"),
            ["elf"] = new(HeroAttributeCatalog.Intuition, "Repräsentation Elf"),
            ["hexe"] = new(HeroAttributeCatalog.Intuition, "Repräsentation Hexe"),
            ["kristallomant"] = new(HeroAttributeCatalog.Intuition, "Repräsentation Kristallomant"),
            ["schamane"] = new(HeroAttributeCatalog.Intuition, "Repräsentation Schamane"),
            ["schelm"] = new(HeroAttributeCatalog.Intuition, "Repräsentation Schelm"),
            ["zaubertanzer"] = new(HeroAttributeCatalog.Intuition, "Repräsentation Zaubertänzer")
        };

    private HeroSpellcastingContext(
        IReadOnlyDictionary<string, string> spellRepresentationsByCanonicalName,
        IReadOnlySet<string> ownRepresentations,
        IReadOnlySet<string> detectedTraditions,
        bool hasMatrixUnderstanding)
    {
        SpellRepresentationsByCanonicalName = spellRepresentationsByCanonicalName;
        OwnRepresentations = ownRepresentations;
        DetectedTraditions = detectedTraditions;
        HasMatrixUnderstanding = hasMatrixUnderstanding;
    }

    public static HeroSpellcastingContext Empty { get; } =
        new(
            new Dictionary<string, string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            false);

    public IReadOnlyDictionary<string, string> SpellRepresentationsByCanonicalName { get; }
    public IReadOnlySet<string> OwnRepresentations { get; }
    public IReadOnlySet<string> DetectedTraditions { get; }
    public bool HasMatrixUnderstanding { get; }

    public static HeroSpellcastingContext Create(Hero? hero)
    {
        if (hero?.SourceXml is not { Length: > 0 })
        {
            return Empty;
        }

        try
        {
            var xml = Encoding.UTF8.GetString(hero.SourceXml);
            var document = XDocument.Parse(xml);

            var spellRepresentations = document.Descendants("zauber")
                .Select(spell => new
                {
                    SpellName = GetChildValue(spell, "name"),
                    Representation = SpellRepresentationText.Canonicalize(
                        GetChildValue(spell, "reprasentation", "repräsentation", "repraesentation"))
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.SpellName) &&
                                !string.IsNullOrWhiteSpace(entry.Representation))
                .GroupBy(entry => TalentCatalogText.CanonicalizeName(entry.SpellName), StringComparer.Ordinal)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => group.First().Representation, StringComparer.Ordinal);

            var ownRepresentations = document.Descendants("sonderfertigkeit")
                .Select(ExtractOwnRepresentation)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(SpellRepresentationText.Canonicalize)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
            var detectedTraditions = DetectTraditions(document);

            var hasMatrixUnderstanding = document.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "sonderfertigkeit", StringComparison.Ordinal) ||
                                  string.Equals(element.Name.LocalName, "vorteil", StringComparison.Ordinal))
                .Any(HasMatrixUnderstandingMarker);

            return new HeroSpellcastingContext(
                spellRepresentations,
                ownRepresentations,
                detectedTraditions,
                hasMatrixUnderstanding);
        }
        catch
        {
            return Empty;
        }
    }

    public bool CanUseOwnRepresentationOnlyOption(
        string spellName,
        bool allowsForeignRepresentationWithMatrixUnderstanding)
    {
        var spellRepresentation = GetSpellRepresentation(spellName);
        if (string.IsNullOrWhiteSpace(spellRepresentation) || OwnRepresentations.Count == 0)
        {
            return false;
        }

        if (OwnRepresentations.Contains(spellRepresentation))
        {
            return true;
        }

        return allowsForeignRepresentationWithMatrixUnderstanding && HasMatrixUnderstanding;
    }

    public bool MatchesRepresentationRestriction(string spellName, SpellRepresentationRestriction restriction)
    {
        if (restriction.AllowedRepresentations.Count == 0 && restriction.DisallowedRepresentations.Count == 0)
        {
            return true;
        }

        var spellRepresentation = GetSpellRepresentation(spellName);
        if (string.IsNullOrWhiteSpace(spellRepresentation))
        {
            return false;
        }

        if (restriction.AllowedRepresentations.Count > 0 &&
            !restriction.AllowedRepresentations.Contains(spellRepresentation, StringComparer.Ordinal))
        {
            return false;
        }

        return !restriction.DisallowedRepresentations.Contains(spellRepresentation, StringComparer.Ordinal);
    }

    public SimultaneousModificationInfo GetSimultaneousModificationInfo(
        string spellName,
        IReadOnlyDictionary<string, int> attributeValues)
    {
        var resolution = ResolveLeadAttribute(spellName);
        if (resolution is null)
        {
            var spellRepresentation = GetSpellRepresentation(spellName);
            return new SimultaneousModificationInfo(
                null,
                string.Equals(spellRepresentation, "geode", StringComparison.Ordinal)
                    ? "Leiteigenschaft für gleichzeitige Modifikationen ist bei Geoden nur mit Zuordnung zu Herren der Erde oder Diener Sumus eindeutig."
                    : "Leiteigenschaft für gleichzeitige Modifikationen konnte nicht automatisch bestimmt werden.");
        }

        var leadAttributeValue = attributeValues.TryGetValue(resolution.LeadAttribute, out var value)
            ? value
            : HeroAttributeCatalog.TryGetDefaultValue(resolution.LeadAttribute, out var defaultValue)
                ? defaultValue
                : 0;
        var maximumSimultaneousModifications = leadAttributeValue >= 15
            ? 3
            : leadAttributeValue >= 14
                ? 2
                : leadAttributeValue >= 13
                    ? 1
                    : 0;

        return new SimultaneousModificationInfo(
            maximumSimultaneousModifications,
            $"Gleichzeitig zulässige Modifikationen: {maximumSimultaneousModifications} (Leiteigenschaft {resolution.LeadAttribute} {leadAttributeValue}; {resolution.SourceLabel}). Varianten zählen mit.");
    }

    private string? GetSpellRepresentation(string spellName)
    {
        return SpellRepresentationsByCanonicalName.TryGetValue(
            TalentCatalogText.CanonicalizeName(spellName),
            out var spellRepresentation)
            ? spellRepresentation
            : null;
    }

    private LeadAttributeResolution? ResolveLeadAttribute(string spellName)
    {
        var spellRepresentation = GetSpellRepresentation(spellName);
        if (!string.IsNullOrWhiteSpace(spellRepresentation))
        {
            var matchingTraditions = TraditionDefinitions
                .Where(definition => DetectedTraditions.Contains(definition.Id) &&
                                     definition.Representations.Contains(spellRepresentation, StringComparer.Ordinal))
                .ToArray();
            if (matchingTraditions.Length == 1)
            {
                return new LeadAttributeResolution(
                    matchingTraditions[0].LeadAttribute,
                    matchingTraditions[0].DisplayName);
            }

            if (matchingTraditions.Length > 1)
            {
                var sharedLeadAttributes = matchingTraditions
                    .Select(definition => definition.LeadAttribute)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (sharedLeadAttributes.Length == 1)
                {
                    return new LeadAttributeResolution(
                        sharedLeadAttributes[0],
                        string.Join(" / ", matchingTraditions.Select(definition => definition.DisplayName)));
                }

                return null;
            }

            if (RepresentationLeadAttributes.TryGetValue(spellRepresentation, out var representationLeadAttribute))
            {
                return representationLeadAttribute;
            }
        }

        if (DetectedTraditions.Count == 1)
        {
            var detectedTradition = TraditionDefinitions.FirstOrDefault(definition =>
                DetectedTraditions.Contains(definition.Id));
            if (detectedTradition is not null)
            {
                return new LeadAttributeResolution(
                    detectedTradition.LeadAttribute,
                    detectedTradition.DisplayName);
            }
        }

        if (OwnRepresentations.Count == 1 &&
            RepresentationLeadAttributes.TryGetValue(OwnRepresentations.First(),
                out var ownRepresentationLeadAttribute))
        {
            return ownRepresentationLeadAttribute;
        }

        return null;
    }

    private static HashSet<string> DetectTraditions(XDocument document)
    {
        var candidateTexts = document
            .Descendants()
            .Where(element => !element.HasElements && !HasExcludedTraditionAncestor(element))
            .Select(element => TalentCatalogText.CanonicalizeText(element.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return TraditionDefinitions
            .Where(definition => candidateTexts.Any(candidateText =>
                definition.DetectionPatterns.Any(pattern =>
                    candidateText.Contains(pattern, StringComparison.Ordinal))))
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasExcludedTraditionAncestor(XElement element)
    {
        return element.Ancestors().Any(ancestor =>
        {
            var canonicalName = TalentCatalogText.CanonicalizeText(ancestor.Name.LocalName);
            return string.Equals(canonicalName, "zauber", StringComparison.Ordinal) ||
                   string.Equals(canonicalName, "talent", StringComparison.Ordinal);
        });
    }

    private static string ExtractOwnRepresentation(XElement ability)
    {
        var label = GetChildValue(ability, "bezeichner");
        var canonicalLabel = TalentCatalogText.CanonicalizeText(label);
        if (!canonicalLabel.StartsWith("reprasentation", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var name = GetChildValue(ability, "name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var extendedName = GetChildValue(ability, "nameausfuehrlich");
        if (!string.IsNullOrWhiteSpace(extendedName))
        {
            return ExtractSuffixAfterColon(extendedName);
        }

        return ExtractSuffixAfterColon(label);
    }

    private static bool HasMatrixUnderstandingMarker(XElement ability)
    {
        return ContainsMatrixUnderstanding(GetChildValue(ability, "name")) ||
               ContainsMatrixUnderstanding(GetChildValue(ability, "bezeichner")) ||
               ContainsMatrixUnderstanding(GetChildValue(ability, "nameausfuehrlich"));
    }

    private static bool ContainsMatrixUnderstanding(string value)
    {
        return TalentCatalogText.CanonicalizeText(value).Contains("matrixverstandnis", StringComparison.Ordinal);
    }

    private static string ExtractSuffixAfterColon(string value)
    {
        var normalized = TalentCatalogText.NormalizeCatalogText(value);
        var colonIndex = normalized.IndexOf(':');
        return colonIndex >= 0
            ? normalized[(colonIndex + 1)..].Trim()
            : normalized;
    }

    private static string GetChildValue(XElement element, params string[] candidateNames)
    {
        foreach (var child in element.Elements())
        {
            if (candidateNames.Any(candidateName =>
                    string.Equals(
                        TalentCatalogText.CanonicalizeText(child.Name.LocalName),
                        TalentCatalogText.CanonicalizeText(candidateName),
                        StringComparison.Ordinal)))
            {
                return child.Value.Trim();
            }
        }

        return string.Empty;
    }

    internal sealed record SimultaneousModificationInfo(int? MaximumSelectableOptions, string? Note);

    private sealed record TraditionDefinition(
        string Id,
        string DisplayName,
        string LeadAttribute,
        string[] Representations,
        string[] DetectionPatterns);

    private sealed record LeadAttributeResolution(string LeadAttribute, string SourceLabel);
}

internal static class SpellRepresentationText
{
    private static readonly (string Key, string[] Patterns)[] RepresentationPatterns =
    [
        ("magier", ["gildenmag", "magier"]),
        ("hexe", ["hexe", "hex"]),
        ("elf", ["elf"]),
        ("druide", ["druid"]),
        ("geode", ["geod"]),
        ("scharlatan", ["scharlat"]),
        ("kristallomant", ["kristallom", "achaz"]),
        ("schelm", ["schelm"]),
        ("borbaradian", ["borbarad"]),
        ("zibilja", ["zibilja", "zibil"]),
        ("zaubertanzer", ["zaubertanz"]),
        ("schamane", ["schaman"]),
        ("derwisch", ["derwisch"])
    ];

    public static string Canonicalize(string? value)
    {
        var canonical = TalentCatalogText.CanonicalizeText(value);
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return string.Empty;
        }

        foreach (var (key, patterns) in RepresentationPatterns)
        {
            if (patterns.Any(pattern => canonical.Contains(pattern, StringComparison.Ordinal)))
            {
                return key;
            }
        }

        return canonical;
    }
}