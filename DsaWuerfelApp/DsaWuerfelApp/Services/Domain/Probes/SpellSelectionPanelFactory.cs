using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class SpellSelectionPanelFactory(
    SpellCatalogStore spellCatalogStore,
    HeroSpellIndexBuilder heroSpellIndexBuilder,
    SpellOptionAvailabilityService spellOptionAvailabilityService)
{
    public SpellSelectionPanelDto? Build(Hero hero, ResolvedProbeData resolvedProbe)
    {
        if (resolvedProbe.Kind != ProbeSelectionKind.Spell ||
            !spellCatalogStore.TryGetEntry(resolvedProbe.BaseName, out var spellEntry))
        {
            return null;
        }

        var knownSpells = heroSpellIndexBuilder.Build(hero);
        if (!TryFindEntry(knownSpells, resolvedProbe.BaseName, out var matchedSpellName, out var spell))
        {
            return null;
        }

        var heroSpellcastingContext = HeroSpellcastingContext.Create(hero);
        var availableModifications = spellOptionAvailabilityService.FilterAvailableOptions(
            matchedSpellName,
            spell,
            knownSpells,
            heroSpellcastingContext,
            spellEntry.Modifications);
        var availableVariants = spellOptionAvailabilityService.FilterAvailableOptions(
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
            groups.Add(BuildGroup(
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
            groups.Add(BuildGroup(
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

    private static SpellOptionGroupDto BuildGroup(
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

    private static int ResolveSpellOptionModifier(TalentData spell, string optionName, out string? specializationName)
    {
        specializationName = spell.Specializations.FirstOrDefault(existingSpecialization =>
            string.Equals(
                TalentCatalogText.CanonicalizeName(existingSpecialization),
                TalentCatalogText.CanonicalizeName(optionName),
                StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(specializationName) ? 0 : -2;
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
