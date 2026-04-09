using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class TalentCatalogService(
    TalentCatalogStore talentCatalogStore,
    SpellCatalogStore spellCatalogStore)
{
    private const string RitualKnowledgeTalentPrefix = "Ritualkenntnis:";
    private const string SelfControlTalentName = "Selbstbeherrschung";
    private const string SenseSharpnessTalentName = "Sinnenschärfe";
    private const string WatchKeepingTalentName = "Wache halten";
    private const string WatchKeepingFallbackProbe = "KL/IN/IN";

    public DicePageContextDto BuildContext(Hero? hero, bool showDebugForcedRolls)
    {
        var hasKnownProbes = hero is not null && (hero.Talente.Count > 0 || hero.Zauber.Count > 0);

        return new DicePageContextDto(
            hero?.Id,
            hero?.Name,
            BuildAttributeValues(hero),
            hasKnownProbes ? BuildKnownProbes(hero!) : DefaultProbeCatalog.CreateEntries(),
            BuildBadTraits(hero),
            hero is null ? "Nach Proben suchen..." : $"Talente und Zauber von {hero.Name} durchsuchen...",
            showDebugForcedRolls);
    }

    public ProbeInfoResultDto BuildProbeInfo(Hero? hero, string probeValue, int modifier, string? badTraitName)
    {
        if (string.IsNullOrWhiteSpace(probeValue))
        {
            return TalentProbeInfoBuilder.BuildEmptySelectionInfo();
        }

        var badTrait = ResolveBadTrait(hero, badTraitName);
        if (hero is not null && TryResolveProbe(hero, probeValue, out var resolvedProbe))
        {
            return TalentProbeInfoBuilder.BuildResolvedProbeInfo(
                hero,
                resolvedProbe,
                badTrait,
                ResolveInfoSections(resolvedProbe),
                modifier);
        }

        return TalentProbeInfoBuilder.BuildFallbackInfo(probeValue, badTrait);
    }

    public ResolvedProbeData ResolveProbe(Hero hero, string probeValue)
    {
        if (TryResolveProbe(hero, probeValue, out var resolvedProbe))
        {
            return resolvedProbe;
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

    private bool TryResolveProbe(Hero hero, string probeValue, out ResolvedProbeData resolvedProbe)
    {
        var selection = ProbeSelectionValue.Parse(probeValue);

        if ((selection.Kind is ProbeSelectionKind.Unknown or ProbeSelectionKind.Talent) &&
            TryResolveTalent(hero, selection, out resolvedProbe))
        {
            return true;
        }

        if ((selection.Kind is ProbeSelectionKind.Unknown or ProbeSelectionKind.Spell) &&
            TryResolveSpell(hero, selection, out resolvedProbe))
        {
            return true;
        }

        resolvedProbe = null!;
        return false;
    }

    private bool TryResolveTalent(Hero hero, ParsedProbeSelection selection, out ResolvedProbeData resolvedProbe)
    {
        if (selection.HasOption && selection.OptionKind != ProbeSelectionOptionKind.Specialization)
        {
            resolvedProbe = null!;
            return false;
        }

        var knownTalents = BuildKnownTalentMap(hero);
        if (!TryFindEntry(knownTalents, selection.ProbeName, out var talentName, out var talentEntry) ||
            !IsTalentRollable(talentName, talentEntry) ||
            !TryResolveSpecializationName(talentEntry.Talent, selection, out var specializationName))
        {
            resolvedProbe = null!;
            return false;
        }

        resolvedProbe = new ResolvedProbeData(
            ProbeSelectionKind.Talent,
            talentName,
            selection.HasOption ? selection.DisplayName : talentName,
            talentEntry.Talent,
            specializationName,
            selection.OptionKind,
            specializationName,
            selection.OptionModifier);
        return true;
    }

    private bool TryResolveSpell(Hero hero, ParsedProbeSelection selection, out ResolvedProbeData resolvedProbe)
    {
        var knownSpells = BuildKnownSpellMap(hero);
        if (!TryFindEntry(knownSpells, selection.ProbeName, out var spellName, out var spell))
        {
            resolvedProbe = null!;
            return false;
        }

        var selectedOptionName = ResolveSelectedSpellOptionName(spellName, spell, selection);
        if (selection.HasOption && string.IsNullOrWhiteSpace(selectedOptionName))
        {
            resolvedProbe = null!;
            return false;
        }

        var specializationName = ResolveMatchingSpellSpecialization(spell, selectedOptionName);
        var specializationModifier = string.IsNullOrWhiteSpace(specializationName) ? 0 : -2;

        resolvedProbe = new ResolvedProbeData(
            ProbeSelectionKind.Spell,
            spellName,
            selection.HasOption
                ? ProbeSelectionValue.FormatSelectionLabel(spellName, selection.OptionKind, selectedOptionName!)
                : spellName,
            spell,
            selectedOptionName,
            selection.OptionKind,
            specializationName,
            specializationModifier);
        return true;
    }

    private IReadOnlyList<ProbeInfoSectionDto> ResolveInfoSections(ResolvedProbeData resolvedProbe)
    {
        return resolvedProbe.Kind switch
        {
            ProbeSelectionKind.Talent when talentCatalogStore.TryGetEntry(resolvedProbe.BaseName, out var talentEntry)
                =>
                talentEntry.InfoSections,
            ProbeSelectionKind.Spell when spellCatalogStore.TryGetEntry(resolvedProbe.BaseName, out var spellEntry) =>
                spellEntry.InfoSections,
            _ => []
        };
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

    private ProbeSearchEntryDto[] BuildKnownProbes(Hero hero)
    {
        var knownTalents = BuildKnownTalentMap(hero);
        var activeAlternatives = BuildActiveAlternativeLookup(knownTalents);

        var talentEntries = knownTalents
            .Where(entry => !IsRitualKnowledgeTalent(entry.Key))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => IsTalentRollable(entry.Key, entry.Value)
                ? new ProbeSearchEntryDto(
                    BuildProbeLabel(entry.Key, entry.Value.Talent),
                    ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Talent, entry.Key),
                    true,
                    BuildTalentSpecializationAlternatives(entry.Key, entry.Value.Talent))
                : BuildInactiveProbeSearchEntry(entry.Key, entry.Value, activeAlternatives));

        var spellEntries = BuildKnownSpellMap(hero)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ProbeSearchEntryDto(
                BuildProbeLabel(entry.Key, entry.Value),
                ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Spell, entry.Key),
                true,
                BuildSpellAlternatives(entry.Key, entry.Value)));

        return talentEntries
            .Concat(spellEntries)
            .OrderBy(entry => entry.DisplayLabel, StringComparer.Ordinal)
            .ToArray();
    }

    private Dictionary<string, KnownTalentEntry> BuildKnownTalentMap(Hero hero)
    {
        var knownTalents = hero.Talente.ToDictionary(
            entry => entry.Key,
            entry => new KnownTalentEntry(CloneProbeData(entry.Value), true),
            StringComparer.Ordinal);

        var heroTalentNamesByCanonical = knownTalents.Keys
            .Select(name => (Name: name, Canonical: TalentCatalogText.CanonicalizeName(name)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Canonical))
            .GroupBy(entry => entry.Canonical, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);

        foreach (var catalogEntry in talentCatalogStore.Entries)
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

    private static Dictionary<string, TalentData> BuildKnownSpellMap(Hero hero)
    {
        return hero.Zauber.ToDictionary(
            entry => entry.Key,
            entry => CloneProbeData(entry.Value),
            StringComparer.Ordinal);
    }

    private static TalentData CloneProbeData(TalentData value)
    {
        return new TalentData
        {
            Wert = value.Wert, Probe = value.Probe, Specializations = value.Specializations.ToArray()
        };
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

        if (talentCatalogStore.TryGetEntry(SenseSharpnessTalentName, out var senseSharpnessCatalogEntry) &&
            !string.IsNullOrWhiteSpace(senseSharpnessCatalogEntry.Probe))
        {
            return senseSharpnessCatalogEntry.Probe;
        }

        if (talentCatalogStore.TryGetEntry(SelfControlTalentName, out var selfControlCatalogEntry) &&
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
                        BuildProbeLabel(selectedTalent.Key, selectedTalent.Value.Talent),
                        ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Talent, selectedTalent.Key));
                },
                StringComparer.Ordinal);
    }

    private bool IsTalentRollable(string talentName, KnownTalentEntry talent)
    {
        if (IsRitualKnowledgeTalent(talentName))
        {
            return false;
        }

        return talent.IsOwnedByHero ||
               talentCatalogStore.TryGetEntry(talentName, out var catalogEntry) && catalogEntry.IsBasisTalent;
    }

    private static bool IsRitualKnowledgeTalent(string talentName)
    {
        return talentName.StartsWith(RitualKnowledgeTalentPrefix, StringComparison.Ordinal);
    }

    private static string BuildProbeLabel(string probeName, TalentData probeData)
    {
        return string.IsNullOrWhiteSpace(probeData.Probe)
            ? $"{probeName} [{probeData.Wert}]"
            : $"{probeName} [{probeData.Wert}] ({probeData.Probe})";
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
        if (!talentCatalogStore.TryGetEntry(talentName, out var catalogEntry) ||
            catalogEntry.AlternativeNames.Count == 0)
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

    private static ProbeSearchAlternativeDto[] BuildTalentSpecializationAlternatives(string talentName,
        TalentData talent)
    {
        return talent.Specializations
            .Where(specialization => !string.IsNullOrWhiteSpace(specialization))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(specialization => specialization, StringComparer.Ordinal)
            .Select(specialization => new ProbeSearchAlternativeDto(
                ProbeSelectionValue.FormatSpecializationLabel(talentName, specialization),
                ProbeSelectionValue.EncodeOption(
                    ProbeSelectionKind.Talent,
                    talentName,
                    ProbeSelectionOptionKind.Specialization,
                    specialization,
                    -2)))
            .ToArray();
    }

    private ProbeSearchAlternativeDto[] BuildSpellAlternatives(string spellName, TalentData spell)
    {
        var alternatives = new List<ProbeSearchAlternativeDto>();
        var coveredSpecializations = new HashSet<string>(StringComparer.Ordinal);

        if (spellCatalogStore.TryGetEntry(spellName, out var catalogEntry))
        {
            alternatives.AddRange(BuildSpellOptionAlternatives(
                spellName,
                spell,
                catalogEntry.Modifications,
                ProbeSelectionOptionKind.SpellModification,
                coveredSpecializations));
            alternatives.AddRange(BuildSpellOptionAlternatives(
                spellName,
                spell,
                catalogEntry.Variants,
                ProbeSelectionOptionKind.SpellVariant,
                coveredSpecializations));
        }

        alternatives.AddRange(BuildRemainingSpellSpecializationAlternatives(spellName, spell, coveredSpecializations));

        return alternatives
            .GroupBy(alternative => alternative.Value, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private IEnumerable<ProbeSearchAlternativeDto> BuildSpellOptionAlternatives(
        string spellName,
        TalentData spell,
        IEnumerable<SpellOptionEntry> options,
        ProbeSelectionOptionKind optionKind,
        ISet<string> coveredSpecializations)
    {
        foreach (var option in options
                     .Where(option => !string.IsNullOrWhiteSpace(option.Name))
                     .OrderBy(option => option.Name, StringComparer.Ordinal))
        {
            var optionModifier = ResolveSpellOptionModifier(spell, option.Name, out var specializationName);
            if (!string.IsNullOrWhiteSpace(specializationName))
            {
                coveredSpecializations.Add(TalentCatalogText.CanonicalizeName(specializationName));
            }

            yield return new ProbeSearchAlternativeDto(
                ProbeSelectionValue.FormatOptionLabel(spellName, optionKind, option.Name),
                ProbeSelectionValue.EncodeOption(
                    ProbeSelectionKind.Spell,
                    spellName,
                    optionKind,
                    option.Name,
                    optionModifier));
        }
    }

    private static IEnumerable<ProbeSearchAlternativeDto> BuildRemainingSpellSpecializationAlternatives(
        string spellName,
        TalentData spell,
        ISet<string> coveredSpecializations)
    {
        return spell.Specializations
            .Where(specialization => !string.IsNullOrWhiteSpace(specialization))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(specialization => specialization, StringComparer.Ordinal)
            .Where(specialization =>
                !coveredSpecializations.Contains(TalentCatalogText.CanonicalizeName(specialization)))
            .Select(specialization => new ProbeSearchAlternativeDto(
                ProbeSelectionValue.FormatSpecializationLabel(spellName, specialization),
                ProbeSelectionValue.EncodeOption(
                    ProbeSelectionKind.Spell,
                    spellName,
                    ProbeSelectionOptionKind.Specialization,
                    specialization,
                    -2)));
    }

    private static int ResolveSpellOptionModifier(
        TalentData spell,
        string optionName,
        out string? specializationName)
    {
        specializationName = ResolveMatchingSpellSpecialization(spell, optionName);
        return string.IsNullOrWhiteSpace(specializationName) ? 0 : -2;
    }

    private static string? ResolveSelectedSpellOptionName(
        string spellName,
        TalentData spell,
        ParsedProbeSelection selection)
    {
        if (!selection.HasOption)
        {
            return null;
        }

        if (selection.OptionKind == ProbeSelectionOptionKind.Specialization)
        {
            return spell.Specializations.FirstOrDefault(existingSpecialization =>
                string.Equals(
                    TalentCatalogText.CanonicalizeText(existingSpecialization),
                    TalentCatalogText.CanonicalizeText(selection.OptionName),
                    StringComparison.Ordinal));
        }

        return TalentCatalogText.NormalizeCatalogText(selection.OptionName);
    }

    private static string? ResolveMatchingSpellSpecialization(TalentData spell, string? optionName)
    {
        if (string.IsNullOrWhiteSpace(optionName))
        {
            return null;
        }

        return spell.Specializations.FirstOrDefault(existingSpecialization =>
            string.Equals(
                TalentCatalogText.CanonicalizeName(existingSpecialization),
                TalentCatalogText.CanonicalizeName(optionName),
                StringComparison.Ordinal));
    }

    private static bool TryResolveSpecializationName(
        TalentData talent,
        ParsedProbeSelection selection,
        out string? specializationName)
    {
        if (!selection.HasOption)
        {
            specializationName = null;
            return true;
        }

        specializationName = selection.OptionKind != ProbeSelectionOptionKind.Specialization
            ? null
            : talent.Specializations.FirstOrDefault(existingSpecialization =>
                string.Equals(
                    TalentCatalogText.CanonicalizeText(existingSpecialization),
                    TalentCatalogText.CanonicalizeText(selection.OptionName),
                    StringComparison.Ordinal));

        return !string.IsNullOrWhiteSpace(specializationName);
    }

    private static bool TryFindEntry<TEntry>(
        IReadOnlyDictionary<string, TEntry> entries,
        string? lookupName,
        out string matchedName,
        out TEntry entry)
    {
        var normalizedLookupName = TalentCatalogText.NormalizeCatalogText(lookupName);
        if (string.IsNullOrWhiteSpace(normalizedLookupName))
        {
            matchedName = string.Empty;
            entry = default!;
            return false;
        }

        if (entries.TryGetValue(normalizedLookupName, out entry!))
        {
            matchedName = normalizedLookupName;
            return true;
        }

        var canonicalLookupName = TalentCatalogText.CanonicalizeName(normalizedLookupName);
        foreach (var existingEntry in entries)
        {
            if (string.Equals(
                    TalentCatalogText.CanonicalizeName(existingEntry.Key),
                    canonicalLookupName,
                    StringComparison.Ordinal))
            {
                matchedName = existingEntry.Key;
                entry = existingEntry.Value;
                return true;
            }
        }

        matchedName = string.Empty;
        entry = default!;
        return false;
    }

    private sealed class KnownTalentEntry(TalentData talent, bool isOwnedByHero)
    {
        public TalentData Talent { get; } = talent;
        public bool IsOwnedByHero { get; } = isOwnedByHero;
    }
}