using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class ProbeSearchEntryFactory(
    TalentCatalogStore talentCatalogStore,
    SpellCatalogStore spellCatalogStore,
    HeroTalentIndexBuilder heroTalentIndexBuilder,
    HeroSpellIndexBuilder heroSpellIndexBuilder)
{
    public ProbeSearchEntryDto[] BuildCatalogProbes()
    {
        var talentEntries = talentCatalogStore.Entries
            .Where(entry => !heroTalentIndexBuilder.IsRitualKnowledgeTalent(entry.Name))
            .Select(entry => BuildCatalogProbeEntry(entry));

        var spellEntries = spellCatalogStore.Entries
            .Select(entry => BuildCatalogProbeEntry(ProbeSelectionKind.Spell, entry.Name, entry.Probe));

        return talentEntries
            .Concat(spellEntries)
            .Where(entry => (entry.IsSelectable || entry.Alternatives.Length > 0) &&
                            !string.IsNullOrWhiteSpace(entry.Value))
            .OrderBy(entry => entry.DisplayLabel, StringComparer.Ordinal)
            .ToArray();
    }

    public ProbeSearchEntryDto[] BuildKnownProbes(Hero hero)
    {
        var knownTalents = heroTalentIndexBuilder.Build(hero);
        var knownSpells = heroSpellIndexBuilder.Build(hero);
        var activeAlternatives = BuildActiveAlternativeLookup(knownTalents);

        var talentEntries = knownTalents
            .Where(entry => !heroTalentIndexBuilder.IsRitualKnowledgeTalent(entry.Key))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => heroTalentIndexBuilder.IsTalentRollable(entry.Key, entry.Value)
                ? BuildTalentSearchEntry(entry.Key, entry.Value.Talent)
                : BuildInactiveProbeSearchEntry(entry.Key, entry.Value, activeAlternatives));

        var spellEntries = knownSpells
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ProbeSearchEntryDto(
                BuildProbeLabel(entry.Key, entry.Value),
                ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Spell, entry.Key),
                true,
                BuildSpellSpecializationAlternatives(entry.Key, entry.Value)));

        return talentEntries
            .Concat(spellEntries)
            .OrderBy(entry => entry.DisplayLabel, StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyDictionary<string, ProbeSearchAlternativeDto> BuildActiveAlternativeLookup(
        IReadOnlyDictionary<string, KnownTalentEntry> knownTalents)
    {
        return knownTalents
            .Where(entry => heroTalentIndexBuilder.IsTalentRollable(entry.Key, entry.Value))
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

    private static string BuildProbeLabel(string probeName, TalentData probeData)
    {
        return string.IsNullOrWhiteSpace(probeData.Probe)
            ? $"{probeName} [{probeData.Wert}]"
            : $"{probeName} [{probeData.Wert}] ({probeData.Probe})";
    }

    private ProbeSearchEntryDto BuildCatalogProbeEntry(TalentCatalogEntry entry)
    {
        return new ProbeSearchEntryDto(
            entry.Name,
            ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Talent, entry.Name),
            ProbeAttributes.TryCreate(entry.Probe) is not null,
            BuildTalentProbeAlternatives(entry.Name, entry.ProbeAlternatives));
    }

    private static ProbeSearchEntryDto BuildCatalogProbeEntry(
        ProbeSelectionKind kind,
        string probeName,
        string probe)
    {
        return new ProbeSearchEntryDto(
            probeName,
            ProbeSelectionValue.EncodeBase(kind, probeName),
            ProbeAttributes.TryCreate(probe) is not null,
            []);
    }

    private ProbeSearchEntryDto BuildTalentSearchEntry(string talentName, TalentData talent)
    {
        return new ProbeSearchEntryDto(
            BuildProbeLabel(talentName, talent),
            ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Talent, talentName),
            HasSelectableTalentProbe(talentName, talent),
            BuildTalentAlternatives(talentName, talent));
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

    private bool HasSelectableTalentProbe(string talentName, TalentData talent)
    {
        if (!string.IsNullOrWhiteSpace(talent.Probe))
        {
            return true;
        }

        return talentCatalogStore.TryGetEntry(talentName, out var catalogEntry) &&
               catalogEntry.ProbeAlternatives.Count == 1 &&
               ProbeAttributes.TryCreate(catalogEntry.ProbeAlternatives[0]) is not null;
    }

    private ProbeSearchAlternativeDto[] BuildTalentAlternatives(
        string talentName,
        TalentData talent)
    {
        var alternatives = new List<ProbeSearchAlternativeDto>();
        if (talentCatalogStore.TryGetEntry(talentName, out var catalogEntry))
        {
            alternatives.AddRange(BuildTalentProbeAlternatives(talentName, catalogEntry.ProbeAlternatives));
        }

        alternatives.AddRange(BuildTalentSpecializationAlternatives(talentName, talent));
        return alternatives
            .GroupBy(alternative => alternative.Value, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static ProbeSearchAlternativeDto[] BuildTalentProbeAlternatives(
        string talentName,
        IReadOnlyList<string> probes)
    {
        return probes.Count <= 1
            ? []
            : probes
                .Where(probe => ProbeAttributes.TryCreate(probe) is not null)
                .Distinct(StringComparer.Ordinal)
                .Select(probe => new ProbeSearchAlternativeDto(
                    $"{talentName} ({probe})",
                    ProbeSelectionValue.EncodeTalentProbe(talentName, probe)))
                .ToArray();
    }

    private ProbeSearchAlternativeDto[] BuildTalentSpecializationAlternatives(
        string talentName,
        TalentData talent)
    {
        return talent.Specializations
            .Where(specialization => !string.IsNullOrWhiteSpace(specialization))
            .Where(specialization => talentCatalogStore.SpecializationRules
                .TryGetAvailableSpecialization(talent, specialization, out _))
            .DistinctBy(TalentCatalogText.CanonicalizeText, StringComparer.Ordinal)
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

    private static ProbeSearchAlternativeDto[] BuildSpellSpecializationAlternatives(
        string spellName,
        TalentData spell)
    {
        return spell.Specializations
            .Where(specialization => !string.IsNullOrWhiteSpace(specialization))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(specialization => specialization, StringComparer.Ordinal)
            .Select(specialization => new ProbeSearchAlternativeDto(
                ProbeSelectionValue.FormatSpecializationLabel(
                    ProbeSelectionKind.Spell,
                    spellName,
                    specialization),
                ProbeSelectionValue.EncodeOption(
                    ProbeSelectionKind.Spell,
                    spellName,
                    ProbeSelectionOptionKind.Specialization,
                    specialization,
                    -2)))
            .ToArray();
    }
}
