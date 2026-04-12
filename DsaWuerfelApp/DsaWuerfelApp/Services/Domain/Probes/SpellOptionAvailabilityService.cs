using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class SpellOptionAvailabilityService(SpellCatalogStore spellCatalogStore)
{
    internal SpellOptionEntry[] FilterAvailableOptions(
        string spellName,
        TalentData spell,
        IReadOnlyDictionary<string, TalentData> knownSpells,
        HeroSpellcastingContext heroSpellcastingContext,
        IEnumerable<SpellOptionEntry> options)
    {
        return options
            .Where(option => IsOptionAvailable(spellName, spell, knownSpells, heroSpellcastingContext, option))
            .ToArray();
    }

    internal bool TryResolveAvailableOption(
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

            if (!IsOptionAvailable(spellName, spell, knownSpells, heroSpellcastingContext, option))
            {
                return false;
            }

            matchedOption = option;
            return true;
        }

        return false;
    }

    private static bool IsOptionAvailable(
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

    private static bool TryParseOwnSpellMinimumRequirement(string requirement, out int minimumSpellValue)
    {
        minimumSpellValue = 0;
        if (!requirement.StartsWith("ZfW von ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(new string(requirement.Where(char.IsDigit).ToArray()), out minimumSpellValue);
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

    private static bool TryFindEntry<TEntry>(
        IReadOnlyDictionary<string, TEntry> entries,
        string? lookupName,
        out string matchedName,
        out TEntry entry)
    {
        return TalentCatalogText.TryFindBestNameMatch(entries, lookupName, out matchedName, out entry!);
    }
}
