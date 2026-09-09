using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class RollHistory
{
    [Parameter] public IReadOnlyList<RollHistoryEntryDto> Entries { get; set; } = Array.Empty<RollHistoryEntryDto>();

    private static string GetEntryClass(RollHistoryEntryDto entry)
    {
        return entry.Context?.Outcome switch
        {
            RollHistoryOutcome.Success => "success",
            RollHistoryOutcome.Failure => "failure",
            RollHistoryOutcome.CriticalSuccess => "critical-success",
            RollHistoryOutcome.Fumble => "fumble",
            _ when entry.Context is null => "legacy",
            _ => "neutral"
        };
    }

    private static string GetStatusClass(RollHistoryEntryDto entry)
    {
        return entry.Context?.Outcome switch
        {
            RollHistoryOutcome.Success => "success",
            RollHistoryOutcome.Failure => "failure",
            RollHistoryOutcome.CriticalSuccess => "critical-success",
            RollHistoryOutcome.Fumble => "fumble",
            _ => "neutral"
        };
    }

    private static string GetStatusText(RollHistoryEntryDto entry)
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
                ? "HELD WIDERSTEHT"
                : "EIGENSCHAFT SETZT SICH DURCH";
        }

        return context.Outcome switch
        {
            RollHistoryOutcome.Success => context.Kind == RollHistoryKind.Spell
                ? $"GELUNGEN · {context.RemainingPoints ?? 0} ZfP*"
                : $"BESTANDEN · {context.RemainingPoints ?? 0} TaP*",
            RollHistoryOutcome.Failure => "MISSLUNGEN",
            RollHistoryOutcome.CriticalSuccess => GetSpecialStatusText("GLÜCKLICHER WURF", context),
            RollHistoryOutcome.Fumble => GetSpecialStatusText("PATZER", context),
            _ => "WURF"
        };
    }

    private static string GetSpecialStatusText(string status, RollHistoryContextDto context)
    {
        if (context.RemainingPoints is not { } remainingPoints ||
            context.Kind is not (RollHistoryKind.Talent or RollHistoryKind.Spell))
        {
            return status;
        }

        var suffix = context.Kind == RollHistoryKind.Spell ? "ZfP*" : "TaP*";
        return $"{status} · {remainingPoints} {suffix}";
    }

    private static string GetDisplayName(RollHistoryEntryDto entry)
    {
        return entry.Context?.DisplayName is { Length: > 0 } displayName
            ? displayName
            : "Rohwurf";
    }

    private static string GetCheckClass(RollHistoryCheckState state)
    {
        return state switch
        {
            RollHistoryCheckState.Compensated => "compensated",
            RollHistoryCheckState.Failed => "failed",
            _ => "within-target"
        };
    }

    private static string GetCheckStateText(RollHistoryCheckState state)
    {
        return state switch
        {
            RollHistoryCheckState.Compensated => "ausgeglichen",
            RollHistoryCheckState.Failed => "fehlgeschlagen",
            _ => "im Ziel"
        };
    }

    private static string GetCheckAriaLabel(RollHistoryCheckDto check)
    {
        return $"{check.Name}: {check.Roll} von {check.TargetValue}, {GetCheckStateText(check.State)}";
    }

    private static string GetEquationAriaLabel(RollHistoryEntryDto entry)
    {
        var rolls = entry.Rolls.Length == 0
            ? "keine Würfel"
            : string.Join(" plus ", entry.Rolls.Select(roll => roll.Value));
        var modifier = entry.Modifier switch
        {
            > 0 => $" plus {entry.Modifier}",
            < 0 => $" minus {Math.Abs(entry.Modifier)}",
            _ => string.Empty
        };

        return $"Rohwurfgleichung: {rolls}{modifier} ergibt {entry.TotalSum}";
    }
}