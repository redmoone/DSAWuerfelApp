using System.Text.Json;
using System.Text.Json.Serialization;

using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class TalentCatalogService(IHostEnvironment environment)
{
    private readonly Lazy<IReadOnlyDictionary<string, TalentCatalogEntry>> _entriesByCanonical =
        new(() => LoadEntries(Path.Combine(environment.ContentRootPath, "Data", "talente_mit_spezialisierungen.json")));

    public DicePageContextDto BuildContext(Hero? hero, bool showDebugForcedRolls)
    {
        return new DicePageContextDto(
            hero?.Id,
            hero?.Name,
            BuildAttributeValues(hero),
            hero is { Talente.Count: > 0 } ? BuildTalentProben(hero) : DefaultProbeCatalog.CreateEntries(),
            BuildBadTraits(hero),
            hero is null ? "Nach Proben suchen..." : $"Talente von {hero.Name} durchsuchen...",
            showDebugForcedRolls);
    }

    public ProbeInfoResultDto BuildProbeInfo(Hero? hero, string probeValue, int modifier, string? badTraitName)
    {
        if (string.IsNullOrWhiteSpace(probeValue))
        {
            return new ProbeInfoResultDto("Bitte zuerst eine Probe auswaehlen.");
        }

        var badTrait = ResolveBadTrait(hero, badTraitName);
        var badTraitText = badTrait is null
            ? string.Empty
            : $" Relevante schlechte Eigenschaft: {badTrait.Name} {badTrait.Value}. Dadurch wird die Talentprobe um {badTrait.TalentModifier} und die Eigenschaftsprobe um {badTrait.AttributeModifier} erschwert.";

        if (hero is not null && TryResolveTalent(hero, probeValue, out var resolvedTalent))
        {
            var probeAttributes = ProbeAttributes.TryCreate(resolvedTalent.Talent.Probe)?.ToArray() ?? [];
            var attributeInfo = probeAttributes.Length == 0
                ? "keine Eigenschaften hinterlegt"
                : string.Join(", ", probeAttributes.Select(attribute =>
                    $"{attribute} {GetAttributeValueText(hero, attribute)}"));
            var effectiveModifier = modifier + (badTrait?.TalentModifier ?? 0);
            var effectiveTalentValue = resolvedTalent.Talent.Wert - effectiveModifier;
            var modifierInfo = effectiveTalentValue >= 0
                ? $"Nach dem Gesamtmodifikator {FormatModifier(effectiveModifier)} bleiben {effectiveTalentValue} Ausgleichspunkte fuer Ueberschreitungen."
                : $"Nach dem Gesamtmodifikator {FormatModifier(effectiveModifier)} liegt der effektive Talentwert bei {effectiveTalentValue}. Dadurch muessen alle drei Eigenschaftswuerfe jeweils um {Math.Abs(effectiveTalentValue)} Punkte niedriger geschafft werden.";

            return new ProbeInfoResultDto(
                $"{resolvedTalent.Name} hat aktuell TaW {resolvedTalent.Talent.Wert}. Probe: {resolvedTalent.Talent.Probe}. Verwendete Eigenschaften: {attributeInfo}. {modifierInfo}{badTraitText}");
        }

        var probe = ExtractProbeFromLabel(probeValue);
        if (!string.IsNullOrWhiteSpace(probe))
        {
            return new ProbeInfoResultDto(
                $"Die ausgewaehlte Probe verwendet {probe}. Fuer heldenspezifische Zusatzinformationen bitte einen aktiven Helden waehlen.{badTraitText}");
        }

        return new ProbeInfoResultDto(
            $"Zur ausgewaehlten Probe '{probeValue}' sind aktuell keine weiteren Informationen verfuegbar.{badTraitText}");
    }

    public ResolvedTalentData ResolveTalent(Hero hero, string talentKey)
    {
        if (TryResolveTalent(hero, talentKey, out var resolvedTalent))
        {
            return resolvedTalent;
        }

        throw new InvalidOperationException("Die ausgewaehlte Probe konnte nicht aufgeloest werden.");
    }

    public BadTraitDto? ResolveBadTrait(Hero? hero, string? badTraitName)
    {
        if (hero is null || string.IsNullOrWhiteSpace(badTraitName))
        {
            return null;
        }

        return hero.SchlechteEigenschaften.TryGetValue(badTraitName, out var value)
            ? BuildBadTrait(badTraitName, value)
            : null;
    }

    private bool TryResolveTalent(Hero hero, string talentKey, out ResolvedTalentData resolvedTalent)
    {
        var knownTalents = BuildKnownTalentMap(hero);

        if (knownTalents.TryGetValue(talentKey, out var talent) && IsTalentRollable(talentKey, talent))
        {
            resolvedTalent = new ResolvedTalentData(talentKey, talent);
            return true;
        }

        var canonicalTalentKey = TalentCatalogText.CanonicalizeName(talentKey);
        foreach (var entry in knownTalents)
        {
            if (string.Equals(
                    TalentCatalogText.CanonicalizeName(entry.Key),
                    canonicalTalentKey,
                    StringComparison.Ordinal) &&
                IsTalentRollable(entry.Key, entry.Value))
            {
                resolvedTalent = new ResolvedTalentData(entry.Key, entry.Value);
                return true;
            }
        }

        resolvedTalent = null!;
        return false;
    }

    private static AttributeValueDto[] BuildAttributeValues(Hero? hero)
    {
        var source = hero?.Eigenschaften?.Count > 0 ? hero.Eigenschaften : HeroAttributeCatalog.DefaultValues;

        return HeroAttributeCatalog.Order
            .Select(attribute => new AttributeValueDto(attribute, source.GetValueOrDefault(attribute)))
            .Concat(source
                .Where(entry => !HeroAttributeCatalog.Order.Contains(entry.Key, StringComparer.Ordinal))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new AttributeValueDto(entry.Key, entry.Value)))
            .ToArray();
    }

    private static BadTraitDto[] BuildBadTraits(Hero? hero)
    {
        if (hero is null || hero.SchlechteEigenschaften.Count == 0)
        {
            return [];
        }

        return hero.SchlechteEigenschaften
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => BuildBadTrait(entry.Key, entry.Value))
            .ToArray();
    }

    private static BadTraitDto BuildBadTrait(string name, int value)
    {
        return new BadTraitDto(name, value, value, value <= 0 ? 0 : (value + 1) / 2);
    }

    private ProbeSearchEntryDto[] BuildTalentProben(Hero hero)
    {
        var knownTalents = BuildKnownTalentMap(hero);
        var activeAlternatives = BuildActiveAlternativeLookup(knownTalents);

        return knownTalents
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => IsTalentRollable(entry.Key, entry.Value)
                ? BuildSelectableProbeSearchEntry(BuildTalentProbeLabel(entry.Key, entry.Value), entry.Key)
                : BuildInactiveProbeSearchEntry(entry.Key, entry.Value, activeAlternatives))
            .ToArray();
    }

    private Dictionary<string, TalentData> BuildKnownTalentMap(Hero hero)
    {
        var knownTalents = hero.Talente.ToDictionary(
            entry => entry.Key,
            entry => new TalentData { Wert = entry.Value.Wert, Probe = entry.Value.Probe },
            StringComparer.Ordinal);

        var heroTalentNamesByCanonical = knownTalents.Keys
            .Select(name => (Name: name, Canonical: TalentCatalogText.CanonicalizeName(name)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Canonical))
            .GroupBy(entry => entry.Canonical, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);

        foreach (var catalogEntry in _entriesByCanonical.Value.Values)
        {
            var canonicalName = TalentCatalogText.CanonicalizeName(catalogEntry.Name);
            if (heroTalentNamesByCanonical.TryGetValue(canonicalName, out var existingTalentName))
            {
                var existingTalent = knownTalents[existingTalentName];
                if (string.IsNullOrWhiteSpace(existingTalent.Probe))
                {
                    existingTalent.Probe = catalogEntry.Probe;
                }

                continue;
            }

            knownTalents[catalogEntry.Name] = new TalentData { Wert = 0, Probe = catalogEntry.Probe };
        }

        return knownTalents;
    }

    private IReadOnlyDictionary<string, ProbeSearchAlternativeDto> BuildActiveAlternativeLookup(
        IReadOnlyDictionary<string, TalentData> knownTalents)
    {
        return knownTalents
            .Where(entry => IsTalentRollable(entry.Key, entry.Value))
            .OrderByDescending(entry => entry.Value.Wert)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .GroupBy(entry => TalentCatalogText.CanonicalizeName(entry.Key), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var selectedTalent = group.First();
                    return new ProbeSearchAlternativeDto(
                        BuildTalentProbeLabel(selectedTalent.Key, selectedTalent.Value),
                        selectedTalent.Key);
                },
                StringComparer.Ordinal);
    }

    private bool IsTalentRollable(string talentName, TalentData talent)
    {
        if (talent.Wert > 0)
        {
            return true;
        }

        return TryGetEntry(talentName, out var catalogEntry) && catalogEntry.IsBasisTalent;
    }

    private static string GetAttributeValueText(Hero hero, string attribute)
    {
        return hero.Eigenschaften.TryGetValue(attribute, out var value) ? value.ToString() : "?";
    }

    private static string BuildTalentProbeLabel(string talentName, TalentData talent)
    {
        return string.IsNullOrWhiteSpace(talent.Probe)
            ? $"{talentName} [{talent.Wert}]"
            : $"{talentName} [{talent.Wert}] ({talent.Probe})";
    }

    private static ProbeSearchEntryDto BuildSelectableProbeSearchEntry(string label, string? value = null)
    {
        return new ProbeSearchEntryDto(label, value ?? label, true, []);
    }

    private ProbeSearchEntryDto BuildInactiveProbeSearchEntry(
        string talentName,
        TalentData talent,
        IReadOnlyDictionary<string, ProbeSearchAlternativeDto> activeAlternatives)
    {
        return new ProbeSearchEntryDto(
            BuildInactiveTalentLabel(talentName, talent),
            null,
            false,
            BuildReplacementAlternatives(talentName, activeAlternatives));
    }

    private ProbeSearchAlternativeDto[] BuildReplacementAlternatives(
        string talentName,
        IReadOnlyDictionary<string, ProbeSearchAlternativeDto> activeAlternatives)
    {
        if (!TryGetEntry(talentName, out var catalogEntry) || catalogEntry.AlternativeNames.Count == 0)
        {
            return [];
        }

        return catalogEntry.AlternativeNames
            .Select(TalentCatalogText.CanonicalizeName)
            .Where(canonicalName => !string.IsNullOrWhiteSpace(canonicalName) &&
                                    activeAlternatives.ContainsKey(canonicalName))
            .Distinct(StringComparer.Ordinal)
            .Select(canonicalName => activeAlternatives[canonicalName])
            .ToArray();
    }

    private static string BuildInactiveTalentLabel(string talentName, TalentData talent)
    {
        return string.IsNullOrWhiteSpace(talent.Probe)
            ? $"{talentName} nicht aktiviert"
            : $"{talentName} nicht aktiviert ({talent.Probe})";
    }

    private bool TryGetEntry(string talentName, out TalentCatalogEntry entry)
    {
        return _entriesByCanonical.Value.TryGetValue(TalentCatalogText.CanonicalizeName(talentName), out entry!);
    }

    private static string? ExtractProbeFromLabel(string label)
    {
        var startIndex = label.LastIndexOf('(');
        var endIndex = label.LastIndexOf(')');

        return startIndex < 0 || endIndex <= startIndex
            ? null
            : label.Substring(startIndex + 1, endIndex - startIndex - 1);
    }

    private static string FormatModifier(int modifier)
    {
        return modifier > 0 ? $"+{modifier}" : modifier.ToString();
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
            TalentCatalogText.ParseAlternatives(item.Ersatz));
    }

    private sealed record TalentCatalogEntry(
        string Name,
        string Probe,
        bool IsBasisTalent,
        IReadOnlyList<string> AlternativeNames);

    public sealed record ResolvedTalentData(string Name, TalentData Talent);

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