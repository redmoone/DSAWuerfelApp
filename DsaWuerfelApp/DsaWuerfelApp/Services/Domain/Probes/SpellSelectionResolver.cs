using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class SpellSelectionResolver(
    HeroSpellIndexBuilder heroSpellIndexBuilder,
    SpellOptionAvailabilityService spellOptionAvailabilityService)
{
    internal bool TryResolve(
        Hero hero,
        string spellName,
        TalentData spell,
        ParsedProbeSelection selection,
        IReadOnlyList<string> spellOptionValues,
        out ResolvedHeroSpellSelection resolvedSelection)
    {
        var heroSpellcastingContext = HeroSpellcastingContext.Create(hero);
        var selectedOptionName = ResolveSelectedSpellOptionName(spellName, spell, selection);
        if (selection.HasOption &&
            selection.OptionKind == ProbeSelectionOptionKind.Specialization &&
            string.IsNullOrWhiteSpace(selectedOptionName))
        {
            resolvedSelection = null!;
            return false;
        }

        var knownSpells = heroSpellIndexBuilder.Build(hero);
        if (!TryResolveSelectedOptions(
                spellName,
                spell,
                knownSpells,
                heroSpellcastingContext,
                selection,
                spellOptionValues,
                out var selectedSpellOptions))
        {
            resolvedSelection = null!;
            return false;
        }

        var simultaneousModificationInfo =
            heroSpellcastingContext.GetSimultaneousModificationInfo(spellName, hero.Eigenschaften);
        if (simultaneousModificationInfo.MaximumSelectableOptions.HasValue &&
            selectedSpellOptions.Length > simultaneousModificationInfo.MaximumSelectableOptions.Value)
        {
            resolvedSelection = null!;
            return false;
        }

        var specializationName = !string.IsNullOrWhiteSpace(selectedOptionName)
            ? ResolveMatchingSpellSpecialization(spell, selectedOptionName)
            : selectedSpellOptions
                .Select(option => ResolveMatchingSpellSpecialization(spell, option.Name))
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        var specializationModifier = string.IsNullOrWhiteSpace(specializationName) ? 0 : -2;

        resolvedSelection = new ResolvedHeroSpellSelection(
            selectedOptionName,
            selectedSpellOptions,
            specializationName,
            specializationModifier,
            BuildResolvedSpellName(spellName, selection, selectedOptionName, selectedSpellOptions));
        return true;
    }

    private bool TryResolveSelectedOptions(
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
            if (!spellOptionAvailabilityService.TryResolveAvailableOption(
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

            if (!spellOptionAvailabilityService.TryResolveAvailableOption(
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

    private static int ResolveSpellOptionModifier(TalentData spell, string optionName, out string? specializationName)
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

    private static string BuildResolvedSpellName(
        string spellName,
        ParsedProbeSelection selection,
        string? selectedOptionName,
        IReadOnlyList<ResolvedSpellOption> selectedSpellOptions)
    {
        var baseLabel = selection.HasOption && selection.OptionKind == ProbeSelectionOptionKind.Specialization
            ? ProbeSelectionValue.FormatSelectionLabel(
                ProbeSelectionKind.Spell,
                spellName,
                selection.OptionKind,
                selectedOptionName ?? selection.OptionName ?? string.Empty)
            : spellName;

        if (selectedSpellOptions.Count == 0)
        {
            return baseLabel;
        }

        return $"{baseLabel} ({string.Join(", ", selectedSpellOptions.Select(option => option.DisplayName))})";
    }
}
