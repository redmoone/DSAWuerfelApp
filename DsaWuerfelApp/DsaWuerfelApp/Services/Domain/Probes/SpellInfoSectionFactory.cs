using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class SpellInfoSectionFactory(
    HeroSpellIndexBuilder heroSpellIndexBuilder,
    SpellOptionAvailabilityService spellOptionAvailabilityService)
{
    public IReadOnlyList<ProbeInfoSectionDto> Build(Hero? hero, string spellName, SpellCatalogEntry spellEntry)
    {
        var sections = new List<ProbeInfoSectionDto>(spellEntry.InfoSections);
        AddRuleSections(sections, spellEntry.RuleSections);

        if (hero is null)
        {
            AddInfoSectionIfPresent(
                sections,
                "Modifikationen",
                BuildOptionSectionText(spellEntry.Modifications));
            AddInfoSectionIfPresent(
                sections,
                "Varianten",
                BuildOptionSectionText(spellEntry.Variants));
            return sections;
        }

        var knownSpells = heroSpellIndexBuilder.Build(hero);
        if (!TryFindEntry(knownSpells, spellName, out var matchedSpellName, out var spell))
        {
            return sections;
        }

        var heroSpellcastingContext = HeroSpellcastingContext.Create(hero);
        var availableModifications = spellOptionAvailabilityService.FilterOptionsForInformation(
            matchedSpellName,
            spell,
            knownSpells,
            heroSpellcastingContext,
            spellEntry.Modifications);
        var availableVariants = spellOptionAvailabilityService.FilterOptionsForInformation(
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
        var manualText = option.IsSelectionEnabled
            ? string.Empty
            : "Manuelle Prüfung erforderlich; automatische Auswahl/Berechnung ist noch nicht hinterlegt.";
        var details = new[] { manualText, option.DisplayText }
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        if (details.Length == 0 ||
            details.Length == 1 && string.Equals(details[0], label, StringComparison.Ordinal))
        {
            return label;
        }

        return $"{label}{Environment.NewLine}{string.Join(Environment.NewLine, details)}";
    }

    private static void AddRuleSections(
        ICollection<ProbeInfoSectionDto> sections,
        IReadOnlyList<SpellInfoRuleSection> ruleSections)
    {
        foreach (var ruleSection in ruleSections)
        {
            AddInfoSectionIfPresent(
                sections,
                ruleSection.Label,
                string.Join($"{Environment.NewLine}{Environment.NewLine}", ruleSection.Entries));
        }
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
