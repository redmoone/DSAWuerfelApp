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

    public static ProbeInfoResultDto BuildResolvedTalentInfo(
        Hero hero,
        ResolvedTalentData resolvedTalent,
        BadTraitDto? badTrait,
        TalentCatalogEntry? catalogEntry,
        int basisModifier)
    {
        var probeAttributes = ProbeAttributes.TryCreate(resolvedTalent.Talent.Probe)?.ToArray() ?? [];
        var effectiveModifier = basisModifier + (badTrait?.TalentModifier ?? 0) + resolvedTalent.SpecializationModifier;

        return new ProbeInfoResultDto(
            BuildSummaryText(hero, resolvedTalent, probeAttributes, effectiveModifier),
            BuildDetailsText(hero, resolvedTalent, probeAttributes, effectiveModifier, badTrait),
            catalogEntry is null ? [] : [.. catalogEntry.InfoSections]);
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
        ResolvedTalentData resolvedTalent,
        IReadOnlyList<string> probeAttributes,
        int effectiveModifier)
    {
        if (probeAttributes.Count != 3)
        {
            return $"Für {resolvedTalent.Name} konnte keine Erfolgschance berechnet werden.";
        }

        var attributeValues = probeAttributes
            .Select(attribute => hero.Eigenschaften.GetValueOrDefault(attribute))
            .ToArray();
        var successChance = TalentProbeEvaluator.CalculateSuccessChance(
            resolvedTalent.Talent.Wert,
            effectiveModifier,
            attributeValues);

        return
            $"{resolvedTalent.Name}: {successChance.ToString("P1", CultureInfo.GetCultureInfo("de-DE"))} Erfolgschance.";
    }

    private static string BuildDetailsText(
        Hero hero,
        ResolvedTalentData resolvedTalent,
        IReadOnlyList<string> probeAttributes,
        int effectiveModifier,
        BadTraitDto? badTrait)
    {
        var attributeInfo = probeAttributes.Count == 0
            ? "keine Eigenschaften hinterlegt"
            : string.Join(", ", probeAttributes.Select(attribute =>
                $"{attribute} {hero.Eigenschaften.GetValueOrDefault(attribute)}"));
        var effectiveTalentValue = resolvedTalent.Talent.Wert - effectiveModifier;
        var availableCompensation = Math.Min(Math.Max(effectiveTalentValue, 0), resolvedTalent.Talent.Wert);
        var modifierInfo = effectiveTalentValue >= 0
            ? $"Nach dem Gesamtmodifikator {FormatModifier(effectiveModifier)} bleiben {availableCompensation} Ausgleichspunkte für Überschreitungen."
            : $"Nach dem Gesamtmodifikator {FormatModifier(effectiveModifier)} liegt der effektive Talentwert bei {effectiveTalentValue}. Dadurch müssen alle drei Eigenschaftswürfe jeweils um {Math.Abs(effectiveTalentValue)} Punkte niedriger geschafft werden.";
        var specializationInfo = resolvedTalent.SpecializationModifier == 0 ||
                                 string.IsNullOrWhiteSpace(resolvedTalent.SpecializationName)
            ? string.Empty
            : $" Gewählte Talentspezialisierung: {resolvedTalent.SpecializationName}. Dadurch ist die Probe um 2 Punkte erleichtert.";

        return
            $"{resolvedTalent.Name} hat aktuell TaW {resolvedTalent.Talent.Wert}. Probe: {resolvedTalent.Talent.Probe}. Verwendete Eigenschaften: {attributeInfo}. {modifierInfo}{specializationInfo}{BuildBadTraitText(badTrait)}";
    }

    private static string BuildBadTraitText(BadTraitDto? badTrait)
    {
        return badTrait is null
            ? string.Empty
            : $" Relevante schlechte Eigenschaft: {badTrait.Name} {badTrait.Value}. Dadurch wird die Talentprobe um {badTrait.TalentModifier} und die Eigenschaftsprobe um {badTrait.AttributeModifier} erschwert.";
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