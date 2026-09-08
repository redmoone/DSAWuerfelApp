using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class ProbeResolutionService(
    TalentCatalogStore talentCatalogStore,
    SpellCatalogStore spellCatalogStore,
    HeroProbeCatalogBuilder heroProbeCatalogBuilder,
    SpellOptionResolver spellOptionResolver)
{
    public bool TryResolveProbe(
        Hero hero,
        string probeValue,
        IReadOnlyList<string>? spellOptionValues,
        out ResolvedProbeData resolvedProbe)
    {
        return TryResolveHeroProbe(hero, probeValue, spellOptionValues ?? [], out resolvedProbe);
    }

    public ResolvedProbeData ResolveProbe(Hero hero, string probeValue, IReadOnlyList<string>? spellOptionValues = null)
    {
        if (TryResolveHeroProbe(hero, probeValue, spellOptionValues ?? [], out var resolvedProbe))
        {
            return resolvedProbe;
        }

        throw new InvalidOperationException("Die ausgewaehlte Probe konnte nicht aufgeloest werden.");
    }

    public ResolvedProbeData ResolveProbeOrCatalog(
        Hero? hero,
        string probeValue,
        IReadOnlyList<string>? spellOptionValues = null)
    {
        if (hero is not null && TryResolveHeroProbe(hero, probeValue, spellOptionValues ?? [], out var resolvedHeroProbe))
        {
            return resolvedHeroProbe;
        }

        if (TryResolveCatalogProbe(probeValue, out var resolvedCatalogProbe))
        {
            return resolvedCatalogProbe;
        }

        throw new InvalidOperationException("Die ausgewaehlte Probe konnte nicht aufgeloest werden.");
    }

    public bool TryResolveMasterProbe(
        Hero hero,
        string probeValue,
        IReadOnlyList<string>? spellOptionValues,
        out ResolvedProbeData resolvedProbe,
        out string? unavailableMessage)
    {
        if (TryResolveHeroProbe(hero, probeValue, spellOptionValues ?? [], out resolvedProbe))
        {
            unavailableMessage = null;
            return true;
        }

        var selection = ProbeSelectionValue.Parse(probeValue);
        if (CanResolveCatalogProbeForMaster(selection) &&
            TryResolveCatalogProbe(probeValue, out resolvedProbe))
        {
            unavailableMessage = null;
            return true;
        }

        resolvedProbe = null!;
        unavailableMessage = BuildMasterProbeUnavailableMessage(selection);
        return false;
    }

    private bool TryResolveHeroProbe(
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

    private bool TryResolveCatalogProbe(string probeValue, out ResolvedProbeData resolvedProbe)
    {
        var selection = ProbeSelectionValue.Parse(probeValue);

        if ((selection.Kind is ProbeSelectionKind.Unknown or ProbeSelectionKind.Talent) &&
            TryResolveCatalogTalent(selection, out resolvedProbe))
        {
            return true;
        }

        if ((selection.Kind is ProbeSelectionKind.Unknown or ProbeSelectionKind.Spell) &&
            TryResolveCatalogSpell(selection, out resolvedProbe))
        {
            return true;
        }

        resolvedProbe = null!;
        return false;
    }

    private bool CanResolveCatalogProbeForMaster(ParsedProbeSelection selection)
    {
        if ((selection.Kind is ProbeSelectionKind.Unknown or ProbeSelectionKind.Talent) &&
            talentCatalogStore.TryGetEntry(selection.ProbeName, out var talentEntry))
        {
            return talentEntry.IsBasisTalent;
        }

        return selection.Kind == ProbeSelectionKind.Spell ||
               selection.Kind == ProbeSelectionKind.Unknown && spellCatalogStore.TryGetEntry(selection.ProbeName, out _);
    }

    private string BuildMasterProbeUnavailableMessage(ParsedProbeSelection selection)
    {
        if (selection.Kind == ProbeSelectionKind.Spell ||
            selection.Kind == ProbeSelectionKind.Unknown && spellCatalogStore.TryGetEntry(selection.ProbeName, out _))
        {
            return "Zauber nicht vorhanden.";
        }

        if (selection.Kind == ProbeSelectionKind.Talent ||
            selection.Kind == ProbeSelectionKind.Unknown && talentCatalogStore.TryGetEntry(selection.ProbeName, out _))
        {
            return "Talent nicht vorhanden.";
        }

        return "Die ausgewaehlte Probe konnte nicht aufgeloest werden.";
    }

    private bool TryResolveTalent(Hero hero, ParsedProbeSelection selection, out ResolvedProbeData resolvedProbe)
    {
        if (selection.HasOption &&
            selection.OptionKind is not ProbeSelectionOptionKind.Specialization and
                not ProbeSelectionOptionKind.TalentProbe)
        {
            resolvedProbe = null!;
            return false;
        }

        var knownTalents = heroProbeCatalogBuilder.BuildKnownTalentMap(hero);
        if (!TryFindEntry(knownTalents, selection.ProbeName, out var talentName, out var talentEntry) ||
            !heroProbeCatalogBuilder.IsTalentRollable(talentName, talentEntry) ||
            !TryResolveTalentProbe(talentName, talentEntry.Talent, selection, out var probe) ||
            !TryResolveSpecializationName(talentEntry.Talent, selection, out var specializationName))
        {
            resolvedProbe = null!;
            return false;
        }

        var probeData = CloneWithProbe(talentEntry.Talent, probe);
        resolvedProbe = new ResolvedProbeData(
            ProbeSelectionKind.Talent,
            talentName,
            selection.HasOption ? selection.DisplayName : talentName,
            probeData,
            selection.HasOption ? selection.OptionName : null,
            selection.OptionKind,
            [],
            specializationName,
            specializationName is null ? 0 : -2);
        return true;
    }

    private bool TryResolveCatalogTalent(ParsedProbeSelection selection, out ResolvedProbeData resolvedProbe)
    {
        if (selection.HasOption &&
            selection.OptionKind is not ProbeSelectionOptionKind.Specialization and
                not ProbeSelectionOptionKind.TalentProbe)
        {
            resolvedProbe = null!;
            return false;
        }

        if (!talentCatalogStore.TryGetEntry(selection.ProbeName, out var talentEntry) ||
            heroProbeCatalogBuilder.IsRitualKnowledgeTalent(talentEntry.Name) ||
            !TryResolveCatalogTalentProbe(talentEntry, selection, out var probe))
        {
            resolvedProbe = null!;
            return false;
        }

        var specializationName = selection.OptionKind == ProbeSelectionOptionKind.Specialization
            ? selection.OptionName
            : null;

        resolvedProbe = new ResolvedProbeData(
            ProbeSelectionKind.Talent,
            talentEntry.Name,
            selection.HasOption ? selection.DisplayName : talentEntry.Name,
            new TalentData
            {
                Wert = 0,
                Probe = probe,
                Specializations = []
            },
            selection.HasOption ? selection.OptionName : null,
            selection.OptionKind,
            [],
            specializationName,
            0);
        resolvedProbe = resolvedProbe with { UsesCatalogValue = true };
        return true;
    }

    private bool TryResolveTalentProbe(
        string talentName,
        TalentData talent,
        ParsedProbeSelection selection,
        out string probe)
    {
        if (!selection.HasOption || selection.OptionKind != ProbeSelectionOptionKind.TalentProbe)
        {
            if (!string.IsNullOrWhiteSpace(talent.Probe))
            {
                probe = talent.Probe;
                return true;
            }

            if (talentCatalogStore.TryGetEntry(talentName, out var catalogEntry) &&
                catalogEntry.ProbeAlternatives.Count == 1)
            {
                probe = catalogEntry.ProbeAlternatives[0];
                return true;
            }

            probe = string.Empty;
            return false;
        }

        if (talentCatalogStore.TryGetEntry(talentName, out var talentCatalogEntry) &&
            talentCatalogEntry.TryGetProbe(selection.OptionName, out probe))
        {
            return true;
        }

        if (string.Equals(
                TalentCatalogText.CanonicalizeText(talent.Probe),
                TalentCatalogText.CanonicalizeText(selection.OptionName),
                StringComparison.Ordinal))
        {
            probe = talent.Probe;
            return true;
        }

        probe = string.Empty;
        return false;
    }

    private static bool TryResolveCatalogTalentProbe(
        TalentCatalogEntry talentEntry,
        ParsedProbeSelection selection,
        out string probe)
    {
        if (selection.HasOption && selection.OptionKind == ProbeSelectionOptionKind.TalentProbe)
        {
            return talentEntry.TryGetProbe(selection.OptionName, out probe);
        }

        if (!string.IsNullOrWhiteSpace(talentEntry.Probe))
        {
            probe = talentEntry.Probe;
            return true;
        }

        if (talentEntry.ProbeAlternatives.Count == 1)
        {
            probe = talentEntry.ProbeAlternatives[0];
            return true;
        }

        probe = string.Empty;
        return false;
    }

    private static TalentData CloneWithProbe(TalentData talent, string probe)
    {
        return new TalentData
        {
            Wert = talent.Wert,
            Probe = probe,
            Specializations = talent.Specializations.ToArray()
        };
    }

    private bool TryResolveSpell(
        Hero hero,
        ParsedProbeSelection selection,
        IReadOnlyList<string> spellOptionValues,
        out ResolvedProbeData resolvedProbe)
    {
        var knownSpells = heroProbeCatalogBuilder.BuildKnownSpellMap(hero);
        if (!TryFindEntry(knownSpells, selection.ProbeName, out var spellName, out var spell))
        {
            resolvedProbe = null!;
            return false;
        }

        if (!spellOptionResolver.TryResolveHeroSelection(
                hero,
                spellName,
                spell,
                selection,
                spellOptionValues,
                out var resolvedSpellSelection))
        {
            resolvedProbe = null!;
            return false;
        }

        resolvedProbe = new ResolvedProbeData(
            ProbeSelectionKind.Spell,
            spellName,
            resolvedSpellSelection.DisplayName,
            spell,
            resolvedSpellSelection.SelectedOptionName,
            selection.OptionKind,
            resolvedSpellSelection.SelectedSpellOptions,
            resolvedSpellSelection.SpecializationName,
            resolvedSpellSelection.SpecializationModifier);
        return true;
    }

    private bool TryResolveCatalogSpell(ParsedProbeSelection selection, out ResolvedProbeData resolvedProbe)
    {
        if (selection.HasOption && selection.OptionKind != ProbeSelectionOptionKind.Specialization)
        {
            resolvedProbe = null!;
            return false;
        }

        if (!spellCatalogStore.TryGetEntry(selection.ProbeName, out var spellEntry) ||
            ProbeAttributes.TryCreate(spellEntry.Probe) is null)
        {
            resolvedProbe = null!;
            return false;
        }

        var specializationName = selection.OptionKind == ProbeSelectionOptionKind.Specialization
            ? selection.OptionName
            : null;

        resolvedProbe = new ResolvedProbeData(
            ProbeSelectionKind.Spell,
            spellEntry.Name,
            selection.HasOption ? selection.DisplayName : spellEntry.Name,
            new TalentData
            {
                Wert = 0,
                Probe = spellEntry.Probe,
                Specializations = []
            },
            specializationName,
            selection.OptionKind,
            [],
            specializationName,
            selection.OptionKind == ProbeSelectionOptionKind.Specialization ? selection.OptionModifier : 0);
        resolvedProbe = resolvedProbe with { UsesCatalogValue = true };
        return true;
    }

    private bool TryResolveSpecializationName(
        TalentData talent,
        ParsedProbeSelection selection,
        out string? specializationName)
    {
        if (!selection.HasOption)
        {
            specializationName = null;
            return true;
        }

        if (selection.OptionKind == ProbeSelectionOptionKind.TalentProbe)
        {
            specializationName = null;
            return true;
        }

        if (selection.OptionKind != ProbeSelectionOptionKind.Specialization)
        {
            specializationName = null;
            return false;
        }

        return talentCatalogStore.SpecializationRules.TryGetAvailableSpecialization(
            talent,
            selection.OptionName,
            out specializationName);
    }

    private static bool TryFindEntry<TEntry>(
        IReadOnlyDictionary<string, TEntry> entries,
        string? lookupName,
        out string matchedName,
        out TEntry entry)
    {
        return TalentCatalogText.TryFindBestNameMatch(entries, lookupName, out matchedName, out entry!);
    }
}
