using System.Globalization;

using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

internal static class TalentProbeInfoBuilder
{
    public static ProbeInfoResultDto BuildEmptySelectionInfo()
    {
        return new ProbeInfoResultDto("Bitte zuerst eine Probe auswählen.", null, [], null);
    }

    public static ProbeInfoResultDto BuildResolvedProbeInfo(
        Hero hero,
        ResolvedProbeData resolvedProbe,
        BadTraitDto? badTrait,
        IReadOnlyList<ProbeInfoSectionDto> infoSections,
        SpellSelectionPanelDto? spellSelection,
        int basisModifier)
    {
        var probeAttributes = ProbeAttributes.TryCreate(resolvedProbe.ProbeData.Probe)?.ToArray() ?? [];
        var effectiveModifier = basisModifier + (badTrait?.TalentModifier ?? 0) + resolvedProbe.SpecializationModifier;

        return new ProbeInfoResultDto(
            BuildSummaryText(hero, resolvedProbe, probeAttributes, effectiveModifier),
            BuildDetailsText(hero, resolvedProbe, probeAttributes, badTrait),
            [.. infoSections],
            spellSelection);
    }

    public static ProbeInfoResultDto BuildFallbackInfo(string probeValue, BadTraitDto? badTrait)
    {
        var badTraitText = BuildBadTraitText(badTrait);
        var probe = ExtractProbeFromLabel(probeValue);

        return !string.IsNullOrWhiteSpace(probe)
            ? new ProbeInfoResultDto(
                "Erfolgschance nur mit aktivem Held verfügbar.",
                $"Die ausgewählte Probe verwendet {probe}. Für heldenspezifische Zusatzinformationen bitte einen aktiven Helden wählen.{badTraitText}",
                [],
                null)
            : new ProbeInfoResultDto(
                $"Für '{probeValue}' ist aktuell keine Erfolgschance verfügbar.",
                $"Zur ausgewählten Probe '{probeValue}' sind aktuell keine weiteren Informationen verfügbar.{badTraitText}",
                [],
                null);
    }

    private static string BuildSummaryText(
        Hero hero,
        ResolvedProbeData resolvedProbe,
        IReadOnlyList<string> probeAttributes,
        int effectiveModifier)
    {
        if (!CanCalculateSuccessChance(hero, probeAttributes))
        {
            return $"Für {resolvedProbe.Name} konnte keine Erfolgschance berechnet werden.";
        }

        var attributeValues = probeAttributes
            .Select(attribute => hero.Eigenschaften.GetValueOrDefault(attribute))
            .ToArray();
        var successChance = TalentProbeEvaluator.CalculateSuccessChance(
            resolvedProbe.ProbeData.Wert,
            effectiveModifier,
            attributeValues);

        return
            $"{resolvedProbe.Name}: {successChance.ToString("P1", CultureInfo.GetCultureInfo("de-DE"))} Erfolgschance.";
    }

    private static string BuildDetailsText(
        Hero hero,
        ResolvedProbeData resolvedProbe,
        IReadOnlyList<string> probeAttributes,
        BadTraitDto? badTrait)
    {
        var supplementalText = $"{BuildSelectedOptionText(resolvedProbe)}{BuildBadTraitText(badTrait)}".TrimStart();

        if (!CanCalculateSuccessChance(hero, probeAttributes))
        {
            var baseText =
                $"{resolvedProbe.Name} hat aktuell {GetValueLabel(resolvedProbe.Kind)} {resolvedProbe.ProbeData.Wert}. Probe: {resolvedProbe.ProbeData.Probe}. Für variable oder unbekannte Eigenschaften kann diese Probe aktuell nicht automatisch berechnet werden.";
            return string.IsNullOrWhiteSpace(supplementalText)
                ? baseText
                : $"{baseText} {supplementalText}";
        }

        return supplementalText;
    }

    private static string BuildSelectedOptionText(ResolvedProbeData resolvedProbe)
    {
        if (resolvedProbe.Kind == ProbeSelectionKind.Spell && resolvedProbe.SelectedSpellOptions.Length > 0)
        {
            return BuildSpellOptionText(resolvedProbe);
        }

        return resolvedProbe.SelectedOptionKind switch
        {
            ProbeSelectionOptionKind.Specialization when !string.IsNullOrWhiteSpace(resolvedProbe.SpecializationName) =>
                $" Gewählte {GetSpecializationLabel(resolvedProbe.Kind)}: {resolvedProbe.SpecializationName}. Dadurch ist die Probe um 2 Punkte erleichtert.",
            _ => string.Empty
        };
    }

    private static string BuildSpellOptionText(ResolvedProbeData resolvedProbe)
    {
        var selectedVariants = resolvedProbe.SelectedSpellOptions
            .Where(option => option.Kind == ProbeSelectionOptionKind.SpellVariant)
            .Select(option => option.Name)
            .ToArray();
        var selectedModifications = resolvedProbe.SelectedSpellOptions
            .Where(option => option.Kind == ProbeSelectionOptionKind.SpellModification)
            .Select(option => option.Name)
            .ToArray();
        var specializationText = resolvedProbe.SpecializationModifier == -2 &&
                                 !string.IsNullOrWhiteSpace(resolvedProbe.SpecializationName)
            ? $" Passende Zauberspezialisierung: {resolvedProbe.SpecializationName}. Dadurch ist die Probe um 2 Punkte erleichtert."
            : string.Empty;

        var parts = new List<string>();
        if (selectedVariants.Length > 0)
        {
            parts.Add($"Gewählte Varianten: {string.Join(", ", selectedVariants)}.");
        }

        if (selectedModifications.Length > 0)
        {
            parts.Add($"Gewählte Modifikationen: {string.Join(", ", selectedModifications)}.");
        }

        return parts.Count == 0
            ? string.Empty
            : $" {string.Join(" ", parts)}{specializationText}";
    }

    private static string GetValueLabel(ProbeSelectionKind kind)
    {
        return kind == ProbeSelectionKind.Spell ? "ZfW" : "TaW";
    }

    private static string GetSpecializationLabel(ProbeSelectionKind kind)
    {
        return kind == ProbeSelectionKind.Spell ? "Zauberspezialisierung" : "Talentspezialisierung";
    }

    private static string BuildBadTraitText(BadTraitDto? badTrait)
    {
        return badTrait is null
            ? string.Empty
            : $" Relevante schlechte Eigenschaft: {badTrait.Name} {badTrait.Value}. Dadurch wird die Probe um {badTrait.TalentModifier} und die Eigenschaftsprobe um {badTrait.AttributeModifier} erschwert.";
    }

    private static bool CanCalculateSuccessChance(Hero hero, IReadOnlyList<string> probeAttributes)
    {
        return probeAttributes.Count == 3 &&
               probeAttributes.All(attribute => hero.Eigenschaften.ContainsKey(attribute));
    }

    private static string? ExtractProbeFromLabel(string label)
    {
        var startIndex = label.LastIndexOf('(');
        var endIndex = label.LastIndexOf(')');

        return startIndex < 0 || endIndex <= startIndex
            ? null
            : label.Substring(startIndex + 1, endIndex - startIndex - 1);
    }
}