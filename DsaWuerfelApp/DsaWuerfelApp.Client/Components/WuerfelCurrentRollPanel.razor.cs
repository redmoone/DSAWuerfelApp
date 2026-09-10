using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class WuerfelCurrentRollPanel
{
    [Parameter] public RollHistoryEntryDto? HistoryEntry { get; set; }
    [Parameter] public FreeRollResultDto? FreeRollResult { get; set; }
    [Parameter] public TalentRollResultDto? TalentResult { get; set; }
    [Parameter] public AttributeRollResultDto? AttributeResult { get; set; }
    [Parameter] public BadTraitRollResultDto? BadTraitResult { get; set; }
    [Parameter] public IReadOnlyList<MasterTalentRollTargetResultDto> MasterTalentResults { get; set; } =
        Array.Empty<MasterTalentRollTargetResultDto>();
    [Parameter] public IReadOnlyList<MasterAttributeRollTargetResultDto> MasterAttributeResults { get; set; } =
        Array.Empty<MasterAttributeRollTargetResultDto>();

    private static string FormatModifier(int modifier)
    {
        return modifier > 0 ? $"+{modifier}" : modifier.ToString();
    }

    private static string GetProbeStatusText(TalentRollResultDto result)
    {
        return result.Status switch
        {
            TalentProbeStatus.Patzer => "PATZER",
            TalentProbeStatus.GluecklicherWurf => "GLÜCKLICHER WURF",
            TalentProbeStatus.Bestanden when result.SpellDetails is not null => $"GELUNGEN · {result.Rest} ZfP*",
            TalentProbeStatus.Bestanden => $"BESTANDEN · {result.Rest} TaP*",
            _ => $"MISSLUNGEN · um {result.Margin}"
        };
    }

    private static string GetProbeEvaluationClass(TalentRollResultDto result)
    {
        return result.Status switch
        {
            TalentProbeStatus.Patzer => "patzer",
            TalentProbeStatus.GluecklicherWurf => "glueck",
            TalentProbeStatus.Bestanden => "success",
            _ => "failure"
        };
    }

    private static string GetProbeRollChipClass(TalentRollResultDto result, TalentRollDetailDto detail)
    {
        return result.Status switch
        {
            TalentProbeStatus.Patzer => "patzer",
            TalentProbeStatus.GluecklicherWurf => "glueck",
            _ => detail.Success ? "success" : "failure"
        };
    }

    private static string GetAttributeStatusText(AttributeRollResultDto result)
    {
        return result.Success
            ? $"BESTANDEN · {result.SuccessCount}/{result.Details.Length}"
            : $"MISSLUNGEN · {result.FailureCount} nicht bestanden";
    }

    private static string GetAttributeRollChipClass(AttributeRollDetailDto detail)
    {
        return detail.Success ? "success" : "failure";
    }

    private static string GetAttributeRequirementChipClass(AttributeRollRequirementDetailDto detail)
    {
        return detail.Difference == 0 ? "success" : "failure";
    }

    private static string GetBadTraitStatusText(BadTraitRollResultDto result)
    {
        return result.Success
            ? "BESTANDEN · HELD WIDERSTEHT"
            : "MISSLUNGEN · EIGENSCHAFT SETZT SICH DURCH";
    }

    private static string GetBadTraitEvaluationClass(BadTraitRollResultDto result)
    {
        return result.Success ? "success" : "failure";
    }

    private static string FormatMasterTarget(string playerName, string? heroName)
    {
        return string.IsNullOrWhiteSpace(heroName)
            ? playerName
            : $"{playerName} · {heroName}";
    }

    private static string GetMasterTalentStatusText(MasterTalentRollTargetResultDto item)
    {
        return !string.IsNullOrWhiteSpace(item.ErrorMessage)
            ? "FEHLER"
            : item.RequirementResult?.Requirement is not null
                ? $"BENOETIGT {item.RequirementResult.Requirement.RequiredTalentValue}"
            : item.Result is null
                ? "OFFEN"
                : GetProbeStatusText(item.Result);
    }

    private static string GetMasterTalentItemClass(MasterTalentRollTargetResultDto item)
    {
        return !string.IsNullOrWhiteSpace(item.ErrorMessage)
            ? "failure"
            : item.RequirementResult?.Requirement is not null
                ? string.Empty
            : item.Result is null
                ? string.Empty
                : GetProbeEvaluationClass(item.Result);
    }

    private static string GetMasterAttributeStatusText(MasterAttributeRollTargetResultDto item)
    {
        return !string.IsNullOrWhiteSpace(item.ErrorMessage)
            ? "FEHLER"
            : item.Result is null
                ? "OFFEN"
                : item.Result.Success
                    ? "BESTANDEN"
                    : "MISSLUNGEN";
    }

    private static string GetMasterAttributeItemClass(MasterAttributeRollTargetResultDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
        {
            return "failure";
        }

        if (item.Result is null)
        {
            return string.Empty;
        }

        return item.Result.Success ? "success" : "failure";
    }

    private static string GetHistoryRollKind(RollHistoryEntryDto entry)
    {
        return entry.Context?.Kind switch
        {
            RollHistoryKind.Talent => "talent",
            RollHistoryKind.Spell => "spell",
            RollHistoryKind.Attribute => "attribute",
            RollHistoryKind.BadTrait => "bad-trait",
            _ => "free"
        };
    }

    private static string GetHistoryDisplayName(RollHistoryEntryDto entry)
    {
        return entry.Context?.DisplayName is { Length: > 0 } displayName
            ? displayName
            : "Rohwurf";
    }

    private static string GetHistoryStatusText(RollHistoryEntryDto entry)
    {
        if (entry.Context is not { } context)
        {
            return "ROHWURF";
        }

        if (context.Kind == RollHistoryKind.Free)
        {
            return "FREIER WURF";
        }

        if (context.Kind == RollHistoryKind.BadTrait)
        {
            return context.Outcome == RollHistoryOutcome.Success
                ? "BESTANDEN · HELD WIDERSTEHT"
                : "MISSLUNGEN · EIGENSCHAFT SETZT SICH DURCH";
        }

        return context.Outcome switch
        {
            RollHistoryOutcome.Success when context.Kind == RollHistoryKind.Spell =>
                $"GELUNGEN · {context.RemainingPoints ?? 0} ZfP*",
            RollHistoryOutcome.Success => $"BESTANDEN · {context.RemainingPoints ?? 0} TaP*",
            RollHistoryOutcome.Failure => "MISSLUNGEN",
            RollHistoryOutcome.CriticalSuccess => "GLÜCKLICHER WURF",
            RollHistoryOutcome.Fumble => "PATZER",
            _ => "WURF"
        };
    }

    private static string GetHistoryEvaluationClass(RollHistoryEntryDto entry)
    {
        return entry.Context?.Outcome switch
        {
            RollHistoryOutcome.Success => "success",
            RollHistoryOutcome.Failure => "failure",
            RollHistoryOutcome.CriticalSuccess => "glueck",
            RollHistoryOutcome.Fumble => "patzer",
            _ => string.Empty
        };
    }

    private static string GetHistoryCheckChipClass(RollHistoryCheckState state)
    {
        return state switch
        {
            RollHistoryCheckState.Compensated => "compensated",
            RollHistoryCheckState.Failed => "failure",
            _ => "success"
        };
    }

    private static string GetHistoryCheckDetailText(RollHistoryCheckDto check)
    {
        var overflow = check.Difference > 0
            ? $"Überschreitung: {check.Difference}"
            : "Keine Überschreitung";
        var state = check.State switch
        {
            RollHistoryCheckState.Compensated => "ausgeglichen",
            RollHistoryCheckState.Failed => "fehlgeschlagen",
            _ => "im Ziel"
        };

        return check.RemainingPoints is { } remainingPoints
            ? $"{overflow} · {state} · Rest: {remainingPoints}"
            : $"{overflow} · {state}";
    }

    private static string GetRequirementChipClass(RollHistoryRequirementCheckDto check)
    {
        return check.Difference == 0 ? "success" : "failure";
    }

    private static RollEquationDto BuildHistoryEquation(RollHistoryEntryDto entry)
    {
        var dice = entry.Rolls
            .GroupBy(roll => roll.Sides)
            .Select(group => new DiceRollGroupDto(group.Key, group.Count()))
            .ToArray();

        return new RollEquationDto(
            dice,
            entry.Modifier,
            entry.Rolls,
            entry.Rolls.Sum(roll => roll.Value),
            entry.TotalSum);
    }

    private static IReadOnlyList<string> GetHistorySnapshotDetails(
        RollHistoryContextDto context,
        RollHistorySnapshotDto snapshot)
    {
        var details = new List<string>();

        AddText(details, "Probe", snapshot.Probe);
        AddValue(details, "Talentwert", snapshot.TalentValue);
        AddValue(details, "Effektiver Talentwert", snapshot.EffectiveTalentValue);
        AddModifier(details, "Basis-Mod.", snapshot.BasisModifier);
        AddModifier(details, "Effektiver Mod.", snapshot.EffectiveModifier);

        if (!string.IsNullOrWhiteSpace(snapshot.SpecializationName))
        {
            details.Add($"Spez.: {snapshot.SpecializationName} ({FormatModifier(snapshot.SpecializationModifier ?? 0)})");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.SchlechteEigenschaftName))
        {
            details.Add(
                $"{snapshot.SchlechteEigenschaftName}: {FormatModifier(snapshot.SchlechteEigenschaftModifier ?? 0)}");
        }

        if (snapshot.SuccessCount is { } successCount && snapshot.FailureCount is { } failureCount)
        {
            details.Add($"Bestanden: {successCount} · Fehlgeschlagen: {failureCount}");
        }

        if (snapshot.Margin is { } margin && margin > 0)
        {
            details.Add($"Abstand: {margin}");
        }

        AddText(details, "Eigenschaft", snapshot.EigenschaftName);
        AddValue(details, "Eigenschaftswert", snapshot.EigenschaftWert);
        AddValue(details, "Zielwert", snapshot.TargetValue);

        if (snapshot.EigenschaftSetztSichDurch is { } setsThrough)
        {
            details.Add(setsThrough ? "Eigenschaft setzt sich durch" : "Held widersteht");
        }

        AddValue(details, "Benötigter Talentwert", snapshot.RequiredTalentValue);
        AddValue(details, "Benötigter Ausgleich", snapshot.RequiredCompensation);
        AddValue(details, "Original-ZfW", snapshot.OriginalZfw);
        AddModifier(details, "Automatisch", snapshot.AutomaticModifier);
        AddModifier(details, "Manuell", snapshot.ManualModifier);
        AddValue(details, "Vorab-ZfP", snapshot.PreRollZfp);
        AddValue(details, "Roh-ZfP", snapshot.RawZfp);
        AddValue(details, "Verfügbare ZfP", snapshot.AvailableZfp);

        if (snapshot.ManualModifierRequired is true)
        {
            details.Add("Manuelle Ergänzung erforderlich");
        }

        if (snapshot.SelectedOptions.Length > 0)
        {
            details.Add($"Zauberoptionen: {string.Join(", ", snapshot.SelectedOptions)}");
        }

        if (context.RemainingPoints is { } remainingPoints)
        {
            var label = context.Kind == RollHistoryKind.Spell ? "ZfP*" : "TaP*";
            details.Add($"Verbleibend: {remainingPoints} {label}");
        }

        return details;
    }

    private static void AddText(ICollection<string> details, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            details.Add($"{label}: {value}");
        }
    }

    private static void AddValue(ICollection<string> details, string label, int? value)
    {
        if (value is { } number)
        {
            details.Add($"{label}: {number}");
        }
    }

    private static void AddModifier(ICollection<string> details, string label, int? value)
    {
        if (value is { } modifier)
        {
            details.Add($"{label}: {FormatModifier(modifier)}");
        }
    }
}
