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
        if (selection.HasOption && selection.OptionKind != ProbeSelectionOptionKind.Specialization)
        {
            resolvedProbe = null!;
            return false;
        }

        var knownTalents = heroProbeCatalogBuilder.BuildKnownTalentMap(hero);
        if (!TryFindEntry(knownTalents, selection.ProbeName, out var talentName, out var talentEntry) ||
            !heroProbeCatalogBuilder.IsTalentRollable(talentName, talentEntry) ||
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

    private bool TryResolveCatalogTalent(ParsedProbeSelection selection, out ResolvedProbeData resolvedProbe)
    {
        if (selection.HasOption && selection.OptionKind != ProbeSelectionOptionKind.Specialization)
        {
            resolvedProbe = null!;
            return false;
        }

        if (!talentCatalogStore.TryGetEntry(selection.ProbeName, out var talentEntry) ||
            heroProbeCatalogBuilder.IsRitualKnowledgeTalent(talentEntry.Name) ||
            ProbeAttributes.TryCreate(talentEntry.Probe) is null)
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
                Probe = talentEntry.Probe,
                Specializations = []
            },
            specializationName,
            selection.OptionKind,
            [],
            specializationName,
            selection.OptionKind == ProbeSelectionOptionKind.Specialization ? selection.OptionModifier : 0);
        return true;
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
        return true;
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
}
