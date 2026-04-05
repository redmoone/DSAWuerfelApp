using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class TalentCatalogService(TalentCatalogStore catalogStore)
{
    private const string SelfControlTalentName = "Selbstbeherrschung";
    private const string SenseSharpnessTalentName = "Sinnenschärfe";
    private const string WatchKeepingTalentName = "Wache halten";
    private const string WatchKeepingFallbackProbe = "KL/IN/IN";

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
            return TalentProbeInfoBuilder.BuildEmptySelectionInfo();
        }

        var badTrait = ResolveBadTrait(hero, badTraitName);
        if (hero is not null && TryResolveTalent(hero, probeValue, out var resolvedTalent))
        {
            catalogStore.TryGetEntry(resolvedTalent.Name, out var catalogEntry);
            return TalentProbeInfoBuilder.BuildResolvedTalentInfo(
                hero,
                resolvedTalent,
                badTrait,
                catalogEntry,
                modifier);
        }

        return TalentProbeInfoBuilder.BuildFallbackInfo(probeValue, badTrait);
    }

    public ResolvedTalentData ResolveTalent(Hero hero, string talentKey)
    {
        if (TryResolveTalent(hero, talentKey, out var resolvedTalent))
        {
            return resolvedTalent;
        }

        throw new InvalidOperationException("Die ausgewählte Probe konnte nicht aufgelöst werden.");
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
        var selection = TalentSelectionValue.Parse(talentKey);
        var lookupTalentKey = string.IsNullOrWhiteSpace(selection.TalentName) ? talentKey : selection.TalentName;

        if (knownTalents.TryGetValue(lookupTalentKey, out var talent) &&
            IsTalentRollable(lookupTalentKey, talent) &&
            TryResolveSpecializationName(talent.Talent, selection, out var specializationName))
        {
            resolvedTalent = new ResolvedTalentData(
                selection.HasSpecialization ? selection.DisplayName : lookupTalentKey,
                talent.Talent,
                specializationName,
                selection.SpecializationModifier);
            return true;
        }

        var canonicalTalentKey = TalentCatalogText.CanonicalizeName(lookupTalentKey);
        foreach (var entry in knownTalents)
        {
            if (string.Equals(
                    TalentCatalogText.CanonicalizeName(entry.Key),
                    canonicalTalentKey,
                    StringComparison.Ordinal) &&
                IsTalentRollable(entry.Key, entry.Value) &&
                TryResolveSpecializationName(entry.Value.Talent, selection, out var matchedSpecializationName))
            {
                resolvedTalent = new ResolvedTalentData(
                    selection.HasSpecialization ? selection.DisplayName : entry.Key,
                    entry.Value.Talent,
                    matchedSpecializationName,
                    selection.SpecializationModifier);
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
                ? new ProbeSearchEntryDto(
                    BuildTalentProbeLabel(entry.Key, entry.Value.Talent),
                    entry.Key,
                    true,
                    BuildSpecializationAlternatives(entry.Key, entry.Value.Talent))
                : BuildInactiveProbeSearchEntry(entry.Key, entry.Value, activeAlternatives))
            .ToArray();
    }

    private Dictionary<string, KnownTalentEntry> BuildKnownTalentMap(Hero hero)
    {
        var knownTalents = hero.Talente.ToDictionary(
            entry => entry.Key,
            entry => new KnownTalentEntry(
                new TalentData
                {
                    Wert = entry.Value.Wert,
                    Probe = entry.Value.Probe,
                    Specializations = entry.Value.Specializations.ToArray()
                },
                true),
            StringComparer.Ordinal);

        var heroTalentNamesByCanonical = knownTalents.Keys
            .Select(name => (Name: name, Canonical: TalentCatalogText.CanonicalizeName(name)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Canonical))
            .GroupBy(entry => entry.Canonical, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);

        foreach (var catalogEntry in catalogStore.Entries)
        {
            var canonicalName = TalentCatalogText.CanonicalizeName(catalogEntry.Name);
            if (heroTalentNamesByCanonical.TryGetValue(canonicalName, out var existingTalentName))
            {
                var existingTalent = knownTalents[existingTalentName];
                if (string.IsNullOrWhiteSpace(existingTalent.Talent.Probe))
                {
                    existingTalent.Talent.Probe = catalogEntry.Probe;
                }

                continue;
            }

            knownTalents[catalogEntry.Name] = new KnownTalentEntry(
                new TalentData { Wert = 0, Probe = catalogEntry.Probe, Specializations = [] },
                false);
        }

        AddDerivedTalents(knownTalents);
        return knownTalents;
    }

    private void AddDerivedTalents(Dictionary<string, KnownTalentEntry> knownTalents)
    {
        if (TryCreateWatchKeepingTalent(knownTalents, out var watchKeepingTalent))
        {
            knownTalents[WatchKeepingTalentName] = new KnownTalentEntry(watchKeepingTalent, true);
        }
    }

    private bool TryCreateWatchKeepingTalent(
        IReadOnlyDictionary<string, KnownTalentEntry> knownTalents,
        out TalentData talent)
    {
        if (!TryGetOwnedTalent(knownTalents, SelfControlTalentName, out var selfControlTalent) ||
            !TryGetOwnedTalent(knownTalents, SenseSharpnessTalentName, out var senseSharpnessTalent))
        {
            talent = null!;
            return false;
        }

        var calculatedValue =
            (selfControlTalent.Talent.Wert + (2 * senseSharpnessTalent.Talent.Wert) + 1) / 3;
        var cappedValue = Math.Min(
            calculatedValue,
            Math.Min(
                selfControlTalent.Talent.Wert * 2,
                senseSharpnessTalent.Talent.Wert * 2));

        talent = new TalentData
        {
            Wert = cappedValue,
            Probe = ResolveWatchKeepingProbe(selfControlTalent.Talent, senseSharpnessTalent.Talent),
            Specializations = []
        };

        return true;
    }

    private bool TryGetOwnedTalent(
        IReadOnlyDictionary<string, KnownTalentEntry> knownTalents,
        string talentName,
        out KnownTalentEntry talent)
    {
        if (knownTalents.TryGetValue(talentName, out talent!) && talent.IsOwnedByHero)
        {
            return true;
        }

        var canonicalTalentName = TalentCatalogText.CanonicalizeName(talentName);
        foreach (var entry in knownTalents)
        {
            if (!entry.Value.IsOwnedByHero)
            {
                continue;
            }

            if (string.Equals(
                    TalentCatalogText.CanonicalizeName(entry.Key),
                    canonicalTalentName,
                    StringComparison.Ordinal))
            {
                talent = entry.Value;
                return true;
            }
        }

        talent = null!;
        return false;
    }

    private string ResolveWatchKeepingProbe(TalentData selfControlTalent, TalentData senseSharpnessTalent)
    {
        if (!string.IsNullOrWhiteSpace(senseSharpnessTalent.Probe))
        {
            return senseSharpnessTalent.Probe;
        }

        if (!string.IsNullOrWhiteSpace(selfControlTalent.Probe))
        {
            return selfControlTalent.Probe;
        }

        if (catalogStore.TryGetEntry(SenseSharpnessTalentName, out var senseSharpnessCatalogEntry) &&
            !string.IsNullOrWhiteSpace(senseSharpnessCatalogEntry.Probe))
        {
            return senseSharpnessCatalogEntry.Probe;
        }

        if (catalogStore.TryGetEntry(SelfControlTalentName, out var selfControlCatalogEntry) &&
            !string.IsNullOrWhiteSpace(selfControlCatalogEntry.Probe))
        {
            return selfControlCatalogEntry.Probe;
        }

        return WatchKeepingFallbackProbe;
    }

    private IReadOnlyDictionary<string, ProbeSearchAlternativeDto> BuildActiveAlternativeLookup(
        IReadOnlyDictionary<string, KnownTalentEntry> knownTalents)
    {
        return knownTalents
            .Where(entry => IsTalentRollable(entry.Key, entry.Value))
            .OrderByDescending(entry => entry.Value.Talent.Wert)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .GroupBy(entry => TalentCatalogText.CanonicalizeName(entry.Key), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var selectedTalent = group.First();
                    return new ProbeSearchAlternativeDto(
                        BuildTalentProbeLabel(selectedTalent.Key, selectedTalent.Value.Talent),
                        selectedTalent.Key);
                },
                StringComparer.Ordinal);
    }

    private bool IsTalentRollable(string talentName, KnownTalentEntry talent)
    {
        return talent.IsOwnedByHero ||
               catalogStore.TryGetEntry(talentName, out var catalogEntry) && catalogEntry.IsBasisTalent;
    }

    private static string BuildTalentProbeLabel(string talentName, TalentData talent)
    {
        return string.IsNullOrWhiteSpace(talent.Probe)
            ? $"{talentName} [{talent.Wert}]"
            : $"{talentName} [{talent.Wert}] ({talent.Probe})";
    }

    private ProbeSearchEntryDto BuildInactiveProbeSearchEntry(
        string talentName,
        KnownTalentEntry talent,
        IReadOnlyDictionary<string, ProbeSearchAlternativeDto> activeAlternatives)
    {
        return new ProbeSearchEntryDto(
            BuildInactiveTalentLabel(talentName, talent.Talent),
            null,
            false,
            BuildReplacementAlternatives(talentName, activeAlternatives));
    }

    private ProbeSearchAlternativeDto[] BuildReplacementAlternatives(
        string talentName,
        IReadOnlyDictionary<string, ProbeSearchAlternativeDto> activeAlternatives)
    {
        if (!catalogStore.TryGetEntry(talentName, out var catalogEntry) || catalogEntry.AlternativeNames.Count == 0)
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

    private static ProbeSearchAlternativeDto[] BuildSpecializationAlternatives(string talentName, TalentData talent)
    {
        if (talent.Specializations.Length == 0)
        {
            return [];
        }

        return talent.Specializations
            .Where(specialization => !string.IsNullOrWhiteSpace(specialization))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(specialization => specialization, StringComparer.Ordinal)
            .Select(specialization => new ProbeSearchAlternativeDto(
                TalentSelectionValue.FormatLabel(talentName, specialization),
                TalentSelectionValue.Encode(talentName, specialization)))
            .ToArray();
    }

    private static bool TryResolveSpecializationName(
        TalentData talent,
        ParsedTalentSelection selection,
        out string? specializationName)
    {
        if (!selection.HasSpecialization)
        {
            specializationName = null;
            return true;
        }

        specializationName = talent.Specializations.FirstOrDefault(existingSpecialization =>
            string.Equals(
                TalentCatalogText.CanonicalizeText(existingSpecialization),
                TalentCatalogText.CanonicalizeText(selection.SpecializationName),
                StringComparison.Ordinal));

        return !string.IsNullOrWhiteSpace(specializationName);
    }

    private sealed class KnownTalentEntry(TalentData talent, bool isOwnedByHero)
    {
        public TalentData Talent { get; } = talent;
        public bool IsOwnedByHero { get; } = isOwnedByHero;
    }
}