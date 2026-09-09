using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class WuerfelCurrentRollPanel
{
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
}
