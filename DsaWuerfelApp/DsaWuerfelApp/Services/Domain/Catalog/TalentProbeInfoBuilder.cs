using System.Globalization;

using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

internal static class TalentProbeInfoBuilder
{
    public static ProbeInfoResultDto BuildEmptySelectionInfo()
    {
        return new ProbeInfoResultDto("Bitte zuerst eine Probe auswählen.", null, []);
    }

    public static ProbeInfoResultDto BuildResolvedProbeInfo(
        Hero hero,
        ResolvedProbeData resolvedProbe,
        BadTraitDto? badTrait,
        IReadOnlyList<ProbeInfoSectionDto> infoSections,
        int basisModifier)
    {
        var probeAttributes = ProbeAttributes.TryCreate(resolvedProbe.ProbeData.Probe)?.ToArray() ?? [];
        var effectiveModifier = basisModifier + (badTrait?.TalentModifier ?? 0) + resolvedProbe.SpecializationModifier;

        return new ProbeInfoResultDto(
            BuildSummaryText(hero, resolvedProbe, probeAttributes, effectiveModifier),
            BuildDetailsText(hero, resolvedProbe, probeAttributes, effectiveModifier, badTrait),
            [.. infoSections]);
    }

    public static ProbeInfoResultDto BuildFallbackInfo(string probeValue, BadTraitDto? badTrait)
    {
        var badTraitText = BuildBadTraitText(badTrait);
        var probe = ExtractProbeFromLabel(probeValue);

        return !string.IsNullOrWhiteSpace(probe)
            ? new ProbeInfoResultDto(
                "Erfolgschance nur mit aktivem Held verfügbar.",
                $"Die ausgewählte Probe verwendet {probe}. Für heldenspezifische Zusatzinformationen bitte einen aktiven Helden wählen.{badTraitText}",
                [])
            : new ProbeInfoResultDto(
                $"Für '{probeValue}' ist aktuell keine Erfolgschance verfügbar.",
                $"Zur ausgewählten Probe '{probeValue}' sind aktuell keine weiteren Informationen verfügbar.{badTraitText}",
                []);
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
        int effectiveModifier,
        BadTraitDto? badTrait)
    {
        if (!CanCalculateSuccessChance(hero, probeAttributes))
        {
            return
                $"{resolvedProbe.Name} hat aktuell {GetValueLabel(resolvedProbe.Kind)} {resolvedProbe.ProbeData.Wert}. Probe: {resolvedProbe.ProbeData.Probe}. Für variable oder unbekannte Eigenschaften kann diese Probe aktuell nicht automatisch berechnet werden.{BuildSelectedOptionText(resolvedProbe)}{BuildBadTraitText(badTrait)}";
        }

        var attributeInfo = probeAttributes.Count == 0
            ? "keine Eigenschaften hinterlegt"
            : string.Join(", ", probeAttributes.Select(attribute =>
                $"{attribute} {hero.Eigenschaften.GetValueOrDefault(attribute)}"));
        var effectiveValue = resolvedProbe.ProbeData.Wert - effectiveModifier;
        var availableCompensation = Math.Max(effectiveValue, 0);
        var modifierInfo = effectiveValue >= 0
            ? $"Nach dem Gesamtmodifikator {FormatModifier(effectiveModifier)} bleiben {availableCompensation} Ausgleichspunkte für Überschreitungen."
            : $"Nach dem Gesamtmodifikator {FormatModifier(effectiveModifier)} liegt der effektive Wert bei {effectiveValue}. Dadurch müssen alle drei Eigenschaftswürfe jeweils um {Math.Abs(effectiveValue)} Punkte niedriger geschafft werden.";

        return
            $"{resolvedProbe.Name} hat aktuell {GetValueLabel(resolvedProbe.Kind)} {resolvedProbe.ProbeData.Wert}. Probe: {resolvedProbe.ProbeData.Probe}. Verwendete Eigenschaften: {attributeInfo}. {modifierInfo}{BuildSelectedOptionText(resolvedProbe)}{BuildBadTraitText(badTrait)}";
    }

    private static string BuildSelectedOptionText(ResolvedProbeData resolvedProbe)
    {
        return resolvedProbe.SelectedOptionKind switch
        {
            ProbeSelectionOptionKind.Specialization when !string.IsNullOrWhiteSpace(resolvedProbe.SpecializationName) =>
                $" Gewählte {GetSpecializationLabel(resolvedProbe.Kind)}: {resolvedProbe.SpecializationName}. Dadurch ist die Probe um 2 Punkte erleichtert.",
            ProbeSelectionOptionKind.SpellModification when !string.IsNullOrWhiteSpace(resolvedProbe.SelectedOptionName)
                =>
                BuildSpellOptionText("Modifikation", resolvedProbe),
            ProbeSelectionOptionKind.SpellVariant when !string.IsNullOrWhiteSpace(resolvedProbe.SelectedOptionName) =>
                BuildSpellOptionText("Variante", resolvedProbe),
            _ => string.Empty
        };
    }

    private static string BuildSpellOptionText(string optionLabel, ResolvedProbeData resolvedProbe)
    {
        var specializationText = resolvedProbe.SpecializationModifier == -2 &&
                                 !string.IsNullOrWhiteSpace(resolvedProbe.SpecializationName)
            ? $" Passende Zauberspezialisierung: {resolvedProbe.SpecializationName}. Dadurch ist die Probe um 2 Punkte erleichtert."
            : string.Empty;

        return $" Gewählte {optionLabel}: {resolvedProbe.SelectedOptionName}.{specializationText}";
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

    private static string FormatModifier(int modifier)
    {
        return modifier > 0 ? $"+{modifier}" : modifier.ToString();
    }
}