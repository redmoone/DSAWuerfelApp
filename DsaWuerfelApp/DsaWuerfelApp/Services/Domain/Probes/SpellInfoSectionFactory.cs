using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class SpellInfoSectionFactory(
    HeroSpellIndexBuilder heroSpellIndexBuilder,
    SpellOptionAvailabilityService spellOptionAvailabilityService)
{
    public IReadOnlyList<ProbeInfoSectionDto> Build(Hero hero, string spellName, SpellCatalogEntry spellEntry)
    {
        var sections = new List<ProbeInfoSectionDto>(spellEntry.InfoSections);
        var knownSpells = heroSpellIndexBuilder.Build(hero);
        if (!TryFindEntry(knownSpells, spellName, out var matchedSpellName, out var spell))
        {
            return sections;
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

        AddInfoSectionIfPresent(sections, "Modifikationen", BuildOptionSectionText(availableModifications));
        AddInfoSectionIfPresent(sections, "Varianten", BuildOptionSectionText(availableVariants));

        return sections;
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

    private static bool TryFindEntry<TEntry>(
        IReadOnlyDictionary<string, TEntry> entries,
        string? lookupName,
        out string matchedName,
        out TEntry entry)
    {
        return TalentCatalogText.TryFindBestNameMatch(entries, lookupName, out matchedName, out entry!);
    }
}
