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

    public ProbeInfoResultDto BuildProbeInfo(
        Hero? hero,
        string probeValue,
        int modifier,
        string? badTraitName,
        IReadOnlyList<string>? spellOptionValues = null)
    {
        if (string.IsNullOrWhiteSpace(probeValue))
        {
            return TalentProbeInfoBuilder.BuildEmptySelectionInfo();
        }

        var badTrait = ResolveBadTrait(hero, badTraitName);
        if (hero is not null && TryResolveProbe(hero, probeValue, spellOptionValues ?? [], out var resolvedProbe))
        {
            var spellSelection = BuildSpellSelectionPanel(hero, resolvedProbe);
            return TalentProbeInfoBuilder.BuildResolvedProbeInfo(
                hero,
                resolvedProbe,
                badTrait,
                ResolveInfoSections(hero, resolvedProbe),
                spellSelection,
                modifier);
        }

        return TalentProbeInfoBuilder.BuildFallbackInfo(probeValue, badTrait);
    }

    public ResolvedProbeData ResolveProbe(Hero hero, string probeValue, IReadOnlyList<string>? spellOptionValues = null)
    {
        if (TryResolveProbe(hero, probeValue, spellOptionValues ?? [], out var resolvedProbe))
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

    private bool TryResolveProbe(
        Hero hero,
        string probeValue,
        IReadOnlyList<string> spellOptionValues,
        out ResolvedProbeData resolvedProbe)
    {
        var selection = ProbeSelectionValue.Parse(probeValue);

        if ((selection.Kind is ProbeSelectionKind.Unknown or ProbeSelectionKind.Talent) &&
            TryResolveTalent(hero, selection, out resolvedProbe))
        {
            return true;
        }

        if ((selection.Kind is ProbeSelectionKind.Unknown or ProbeSelectionKind.Spell) &&
            TryResolveSpell(hero, selection, spellOptionValues, out resolvedProbe))
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
            [],
            specializationName,
            selection.OptionModifier);
        return true;
    }

    private bool TryResolveSpell(
        Hero hero,
        ParsedProbeSelection selection,
        IReadOnlyList<string> spellOptionValues,
        out ResolvedProbeData resolvedProbe)
    {
        var knownSpells = BuildKnownSpellMap(hero);
        if (!TryFindEntry(knownSpells, selection.ProbeName, out var spellName, out var spell))
        {
            resolvedProbe = null!;
            return false;
        }

        var heroSpellcastingContext = HeroSpellcastingContext.Create(hero);
        var selectedOptionName = ResolveSelectedSpellOptionName(spellName, spell, selection);
        if (selection.HasOption &&
            selection.OptionKind == ProbeSelectionOptionKind.Specialization &&
            string.IsNullOrWhiteSpace(selectedOptionName))
        {
            resolvedProbe = null!;
            return false;
        }

        if (!TryResolveSelectedSpellOptions(
                spellName,
                spell,
                knownSpells,
                heroSpellcastingContext,
                selection,
                spellOptionValues,
                out var selectedSpellOptions))
        {
            resolvedProbe = null!;
            return false;
        }

        var simultaneousModificationInfo =
            heroSpellcastingContext.GetSimultaneousModificationInfo(spellName, hero.Eigenschaften);
        if (simultaneousModificationInfo.MaximumSelectableOptions.HasValue &&
            selectedSpellOptions.Length > simultaneousModificationInfo.MaximumSelectableOptions.Value)
        {
            resolvedProbe = null!;
            return false;
        }

        var specializationName = !string.IsNullOrWhiteSpace(selectedOptionName)
            ? ResolveMatchingSpellSpecialization(spell, selectedOptionName)
            : selectedSpellOptions
                .Select(option => ResolveMatchingSpellSpecialization(spell, option.Name))
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        var specializationModifier = string.IsNullOrWhiteSpace(specializationName) ? 0 : -2;

        resolvedProbe = new ResolvedProbeData(
            ProbeSelectionKind.Spell,
            spellName,
            BuildResolvedSpellName(spellName, selection, selectedOptionName, selectedSpellOptions),
            spell,
            selectedOptionName,
            selection.OptionKind,
            selectedSpellOptions,
            specializationName,
            specializationModifier);
        return true;
    }

    private IReadOnlyList<ProbeInfoSectionDto> ResolveInfoSections(Hero hero, ResolvedProbeData resolvedProbe)
    {
        return resolvedProbe.Kind switch
        {
            ProbeSelectionKind.Talent when talentCatalogStore.TryGetEntry(resolvedProbe.BaseName, out var talentEntry)
                =>
                talentEntry.InfoSections,
            ProbeSelectionKind.Spell when spellCatalogStore.TryGetEntry(resolvedProbe.BaseName, out var spellEntry) =>
                BuildSpellInfoSections(hero, resolvedProbe.BaseName, spellEntry),
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
        var knownSpells = BuildKnownSpellMap(hero);
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

    private Dictionary<string, KnownTalentEntry> BuildKnownTalentMap(Hero hero)
    {
        var knownTalents = hero.Talente.ToDictionary(
            entry => entry.Key,
            entry => new KnownTalentEntry(CloneProbeData(entry.Value), true),
            StringComparer.Ordinal);

        foreach (var catalogEntry in talentCatalogStore.Entries)
        {
            if (TalentCatalogText.TryFindBestNameMatch(hero.Talente, catalogEntry.Name, out var existingTalentName,
                    out _))
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
        if (TalentCatalogText.TryFindBestNameMatch(
                knownTalents.Where(entry => entry.Value.IsOwnedByHero),
                static existingEntry => existingEntry.Key,
                talentName,
                out var matchedEntry))
        {
            talent = matchedEntry.Value;
            return true;
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

    private bool TryResolveSelectedSpellOptions(
        string spellName,
        TalentData spell,
        IReadOnlyDictionary<string, TalentData> knownSpells,
        HeroSpellcastingContext heroSpellcastingContext,
        ParsedProbeSelection selection,
        IReadOnlyList<string> spellOptionValues,
        out ResolvedSpellOption[] selectedSpellOptions)
    {
        var resolvedOptions = new List<ResolvedSpellOption>();

        if (selection.HasOption &&
            selection.OptionKind is ProbeSelectionOptionKind.SpellModification or ProbeSelectionOptionKind.SpellVariant)
        {
            if (!TryResolveAvailableSpellOption(
                    spellName,
                    spell,
                    knownSpells,
                    heroSpellcastingContext,
                    selection.OptionKind,
                    selection.OptionName!,
                    out var matchedLegacyOption))
            {
                selectedSpellOptions = [];
                return false;
            }

            resolvedOptions.Add(new ResolvedSpellOption(
                matchedLegacyOption.Name,
                matchedLegacyOption.DisplayLabel,
                selection.OptionKind,
                ResolveSpellOptionModifier(spell, matchedLegacyOption.Name, out _)));
        }

        foreach (var spellOptionValue in spellOptionValues)
        {
            if (!ProbeSelectionValue.TryParseSpellOption(spellOptionValue, out var parsedOption))
            {
                selectedSpellOptions = [];
                return false;
            }

            if (!string.Equals(
                    TalentCatalogText.CanonicalizeName(parsedOption.ProbeName),
                    TalentCatalogText.CanonicalizeName(spellName),
                    StringComparison.Ordinal))
            {
                selectedSpellOptions = [];
                return false;
            }

            if (!TryResolveAvailableSpellOption(
                    spellName,
                    spell,
                    knownSpells,
                    heroSpellcastingContext,
                    parsedOption.OptionKind,
                    parsedOption.OptionName,
                    out var matchedOption))
            {
                selectedSpellOptions = [];
                return false;
            }

            var canonicalMatchedOptionName = TalentCatalogText.CanonicalizeName(matchedOption.Name);
            if (resolvedOptions.Any(existingOption =>
                    existingOption.Kind == parsedOption.OptionKind &&
                    string.Equals(
                        TalentCatalogText.CanonicalizeName(existingOption.Name),
                        canonicalMatchedOptionName,
                        StringComparison.Ordinal)))
            {
                continue;
            }

            resolvedOptions.Add(new ResolvedSpellOption(
                matchedOption.Name,
                matchedOption.DisplayLabel,
                parsedOption.OptionKind,
                ResolveSpellOptionModifier(spell, matchedOption.Name, out _)));
        }

        selectedSpellOptions = resolvedOptions.ToArray();
        return true;
    }

    private static string BuildResolvedSpellName(
        string spellName,
        ParsedProbeSelection selection,
        string? selectedOptionName,
        IReadOnlyList<ResolvedSpellOption> selectedSpellOptions)
    {
        var baseLabel = selection.HasOption && selection.OptionKind == ProbeSelectionOptionKind.Specialization
            ? ProbeSelectionValue.FormatSelectionLabel(ProbeSelectionKind.Spell, spellName, selection.OptionKind,
                selectedOptionName ?? selection.OptionName ?? string.Empty)
            : spellName;

        if (selectedSpellOptions.Count == 0)
        {
            return baseLabel;
        }

        return $"{baseLabel} ({string.Join(", ", selectedSpellOptions.Select(option => option.DisplayName))})";
    }

    private IReadOnlyList<ProbeInfoSectionDto> BuildSpellInfoSections(
        Hero hero,
        string spellName,
        SpellCatalogEntry spellEntry)
    {
        var sections = new List<ProbeInfoSectionDto>(spellEntry.InfoSections);
        var knownSpells = BuildKnownSpellMap(hero);
        if (!TryFindEntry(knownSpells, spellName, out var matchedSpellName, out var spell))
        {
            return sections;
        }

        var heroSpellcastingContext = HeroSpellcastingContext.Create(hero);
        var availableModifications = FilterAvailableSpellOptions(
            matchedSpellName,
            spell,
            knownSpells,
            heroSpellcastingContext,
            spellEntry.Modifications);
        var availableVariants = FilterAvailableSpellOptions(
            matchedSpellName,
            spell,
            knownSpells,
            heroSpellcastingContext,
            spellEntry.Variants);

        AddInfoSectionIfPresent(sections, "Modifikationen", BuildOptionSectionText(availableModifications));
        AddInfoSectionIfPresent(sections, "Varianten", BuildOptionSectionText(availableVariants));

        return sections;
    }

    private SpellSelectionPanelDto? BuildSpellSelectionPanel(Hero hero, ResolvedProbeData resolvedProbe)
    {
        if (resolvedProbe.Kind != ProbeSelectionKind.Spell ||
            !spellCatalogStore.TryGetEntry(resolvedProbe.BaseName, out var spellEntry))
        {
            return null;
        }

        var knownSpells = BuildKnownSpellMap(hero);
        if (!TryFindEntry(knownSpells, resolvedProbe.BaseName, out var matchedSpellName, out var spell))
        {
            return null;
        }

        var heroSpellcastingContext = HeroSpellcastingContext.Create(hero);
        var availableModifications = FilterAvailableSpellOptions(
            matchedSpellName,
            spell,
            knownSpells,
            heroSpellcastingContext,
            spellEntry.Modifications);
        var availableVariants = FilterAvailableSpellOptions(
            matchedSpellName,
            spell,
            knownSpells,
            heroSpellcastingContext,
            spellEntry.Variants);
        var simultaneousModificationInfo =
            heroSpellcastingContext.GetSimultaneousModificationInfo(matchedSpellName, hero.Eigenschaften);
        var selectedOptionCount = resolvedProbe.SelectedSpellOptions.Length;

        var groups = new List<SpellOptionGroupDto>();
        if (availableModifications.Length > 0)
        {
            groups.Add(BuildSpellOptionGroup(
                "Spontane Modifikationen",
                matchedSpellName,
                spell,
                resolvedProbe,
                availableModifications,
                ProbeSelectionOptionKind.SpellModification,
                simultaneousModificationInfo.MaximumSelectableOptions,
                selectedOptionCount));
        }

        if (availableVariants.Length > 0)
        {
            groups.Add(BuildSpellOptionGroup(
                "Varianten",
                matchedSpellName,
                spell,
                resolvedProbe,
                availableVariants,
                ProbeSelectionOptionKind.SpellVariant,
                simultaneousModificationInfo.MaximumSelectableOptions,
                selectedOptionCount));
        }

        if (groups.Count == 0)
        {
            return null;
        }

        return new SpellSelectionPanelDto(
            [.. groups],
            simultaneousModificationInfo.Note,
            simultaneousModificationInfo.MaximumSelectableOptions,
            selectedOptionCount);
    }

    private static SpellOptionGroupDto BuildSpellOptionGroup(
        string label,
        string spellName,
        TalentData spell,
        ResolvedProbeData resolvedProbe,
        IEnumerable<SpellOptionEntry> options,
        ProbeSelectionOptionKind optionKind,
        int? maximumSelectableOptions,
        int selectedOptionCount)
    {
        return new SpellOptionGroupDto(
            label,
            options
                .Where(option => !string.IsNullOrWhiteSpace(option.Name))
                .OrderBy(option => option.DisplayLabel, StringComparer.Ordinal)
                .Select(option =>
                {
                    var optionModifier = ResolveSpellOptionModifier(spell, option.Name, out _);
                    var isSelected = resolvedProbe.SelectedSpellOptions.Any(selectedOption =>
                        selectedOption.Kind == optionKind &&
                        string.Equals(
                            TalentCatalogText.CanonicalizeName(selectedOption.Name),
                            TalentCatalogText.CanonicalizeName(option.Name),
                            StringComparison.Ordinal));
                    return new SpellOptionButtonDto(
                        option.DisplayLabel,
                        ProbeSelectionValue.EncodeOption(
                            ProbeSelectionKind.Spell,
                            spellName,
                            optionKind,
                            option.Name,
                            optionModifier),
                        isSelected,
                        !isSelected && maximumSelectableOptions.HasValue &&
                        selectedOptionCount >= maximumSelectableOptions.Value,
                        option.DisplayText);
                })
                .ToArray());
    }

    private static SpellOptionEntry[] FilterAvailableSpellOptions(
        string spellName,
        TalentData spell,
        IReadOnlyDictionary<string, TalentData> knownSpells,
        HeroSpellcastingContext heroSpellcastingContext,
        IEnumerable<SpellOptionEntry> options)
    {
        return options
            .Where(option => IsSpellOptionAvailable(spellName, spell, knownSpells, heroSpellcastingContext, option))
            .ToArray();
    }

    private static bool IsSpellOptionAvailable(
        string spellName,
        TalentData spell,
        IReadOnlyDictionary<string, TalentData> knownSpells,
        HeroSpellcastingContext heroSpellcastingContext,
        SpellOptionEntry option)
    {
        var requirement = option.Requirement;
        if (spell.Wert < requirement.MinimumSpellValue)
        {
            return false;
        }

        if (requirement.RequiresOwnRepresentation &&
            !heroSpellcastingContext.CanUseOwnRepresentationOnlyOption(
                spellName,
                requirement.AllowsForeignRepresentationWithMatrixUnderstanding))
        {
            return false;
        }

        if (!heroSpellcastingContext.MatchesRepresentationRestriction(
                spellName,
                requirement.RepresentationRestriction))
        {
            return false;
        }

        if (!requirement.AdditionalRequirements.All(additionalRequirement =>
                IsAdditionalRequirementSatisfied(additionalRequirement, spell, knownSpells)))
        {
            return false;
        }

        return requirement.Modes.Count == 0 || requirement.Modes.Any(mode => spell.Wert >= mode.MinimumSpellValue);
    }

    private static bool IsAdditionalRequirementSatisfied(
        string requirement,
        TalentData spell,
        IReadOnlyDictionary<string, TalentData> knownSpells)
    {
        var normalizedRequirement = TalentCatalogText.NormalizeCatalogText(requirement);
        if (string.IsNullOrWhiteSpace(normalizedRequirement))
        {
            return true;
        }

        if (TryParseOwnSpellMinimumRequirement(normalizedRequirement, out var ownSpellMinimum))
        {
            return spell.Wert >= ownSpellMinimum;
        }

        if (TryParseOtherSpellMinimumRequirement(normalizedRequirement, knownSpells, out var requiredSpellName,
                out var requiredMinimum))
        {
            return TryFindEntry(knownSpells, requiredSpellName, out _, out var requiredSpell) &&
                   requiredSpell.Wert >= requiredMinimum;
        }

        return IsInformationalAdditionalRequirement(normalizedRequirement);
    }

    private bool TryResolveAvailableSpellOption(
        string spellName,
        TalentData spell,
        IReadOnlyDictionary<string, TalentData> knownSpells,
        HeroSpellcastingContext heroSpellcastingContext,
        ProbeSelectionOptionKind optionKind,
        string optionName,
        out SpellOptionEntry matchedOption)
    {
        matchedOption = null!;

        if (!spellCatalogStore.TryGetEntry(spellName, out var catalogEntry))
        {
            return false;
        }

        var options = optionKind == ProbeSelectionOptionKind.SpellModification
            ? catalogEntry.Modifications
            : catalogEntry.Variants;

        foreach (var option in options)
        {
            if (!string.Equals(
                    TalentCatalogText.CanonicalizeName(option.Name),
                    TalentCatalogText.CanonicalizeName(optionName),
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsSpellOptionAvailable(spellName, spell, knownSpells, heroSpellcastingContext, option))
            {
                return false;
            }

            matchedOption = option;
            return true;
        }

        return false;
    }

    private static bool TryParseOwnSpellMinimumRequirement(string requirement, out int minimumSpellValue)
    {
        minimumSpellValue = 0;
        if (!requirement.StartsWith("ZfW von ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(
            new string(requirement.Where(char.IsDigit).ToArray()),
            out minimumSpellValue);
    }

    private static bool TryParseOtherSpellMinimumRequirement(
        string requirement,
        IReadOnlyDictionary<string, TalentData> knownSpells,
        out string spellName,
        out int minimumSpellValue)
    {
        spellName = string.Empty;
        minimumSpellValue = 0;

        if (!requirement.StartsWith("ZfW ", StringComparison.OrdinalIgnoreCase) ||
            requirement.StartsWith("ZfW von ", StringComparison.OrdinalIgnoreCase) ||
            requirement.Contains(" mal ", StringComparison.OrdinalIgnoreCase) ||
            requirement.Contains(" x ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lastSpaceIndex = requirement.LastIndexOf(' ');
        if (lastSpaceIndex <= 4 || lastSpaceIndex >= requirement.Length - 1)
        {
            return false;
        }

        var numericPart = new string(requirement[(lastSpaceIndex + 1)..].Where(char.IsDigit).ToArray());
        if (!int.TryParse(numericPart, out minimumSpellValue))
        {
            return false;
        }

        var candidateSpellName = requirement[4..lastSpaceIndex].Trim();
        if (string.IsNullOrWhiteSpace(candidateSpellName))
        {
            return false;
        }

        if (!TryFindEntry(knownSpells, candidateSpellName, out spellName, out _))
        {
            return false;
        }

        return true;
    }

    private static bool IsInformationalAdditionalRequirement(string requirement)
    {
        return requirement.Contains(" mal ", StringComparison.OrdinalIgnoreCase) ||
               requirement.Contains(" x ", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOptionSectionText(IEnumerable<SpellOptionEntry> options)
    {
        return string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            options.Select(BuildOptionText).Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string BuildOptionText(SpellOptionEntry option)
    {
        var label = string.IsNullOrWhiteSpace(option.DisplayLabel) ? option.Name : option.DisplayLabel;
        if (string.IsNullOrWhiteSpace(option.DisplayText) ||
            string.Equals(option.DisplayText, label, StringComparison.Ordinal))
        {
            return label;
        }

        return $"{label}{Environment.NewLine}{option.DisplayText}";
    }

    private static void AddInfoSectionIfPresent(ICollection<ProbeInfoSectionDto> sections, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        sections.Add(new ProbeInfoSectionDto(label, value));
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
        return TalentCatalogText.TryFindBestNameMatch(entries, lookupName, out matchedName, out entry!);
    }

    private sealed class KnownTalentEntry(TalentData talent, bool isOwnedByHero)
    {
        public TalentData Talent { get; } = talent;
        public bool IsOwnedByHero { get; } = isOwnedByHero;
    }
}
