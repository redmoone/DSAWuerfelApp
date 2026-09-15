using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class RollHistory
{
    [Parameter] public IReadOnlyList<RollHistoryEntryDto> Entries { get; set; } = Array.Empty<RollHistoryEntryDto>();
    [Parameter] public EventCallback<RollHistoryEntryDto> EntrySelected { get; set; }

    private static string GetHistoryEntryId(RollHistoryEntryDto entry)
    {
        var stateChange = entry.Context?.Snapshot?.CombatStateChange;
        if (stateChange?.Id is { } stateChangeId && stateChangeId != Guid.Empty)
        {
            return $"combat-state-{stateChangeId:N}";
        }

        var combat = entry.Context?.Snapshot?.Combat;
        if (combat?.EntryId is { } entryId && entryId != Guid.Empty)
        {
            return $"combat-{entryId:N}";
        }

        if (combat?.RequestId is { } requestId && requestId != Guid.Empty)
        {
            return $"combat-request-{requestId:N}";
        }

        return entry.Timestamp.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GetEntryClass(RollHistoryEntryDto entry)
    {
        if (IsStateChange(entry))
        {
            return "state-change";
        }

        if (IsInitiativeEntry(entry))
        {
            return "initiative";
        }

        return GetVisualOutcome(entry) switch
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
        return GetVisualOutcome(entry) switch
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

        if (GetCombatStateChange(entry) is { } stateChange)
        {
            return stateChange.IsUndo ? "RÜCKNAHME" : "KAMPFSTATUS";
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

        if (context.Kind == RollHistoryKind.Attribute)
        {
            return context.Outcome switch
            {
                RollHistoryOutcome.CriticalSuccess => GetSpecialStatusText("GLÜCKLICHER WURF", context),
                RollHistoryOutcome.Fumble => GetSpecialStatusText("PATZER", context),
                _ when context.Snapshot?.RequiredTalentValue is { } requiredTalentValue =>
                    $"BENÖTIGT {requiredTalentValue}",
                _ => "EIGENSCHAFTSWURF"
            };
        }

        return context.Outcome switch
        {
            RollHistoryOutcome.Success => context.Kind == RollHistoryKind.Spell
                ? $"GELUNGEN · {context.RemainingPoints ?? 0} ZfP*"
                : context.Kind == RollHistoryKind.Talent
                    ? $"BESTANDEN · {context.RemainingPoints ?? 0} TaP*"
                    : "BESTANDEN",
            RollHistoryOutcome.Failure => "MISSLUNGEN",
            RollHistoryOutcome.CriticalSuccess => GetSpecialStatusText("GLÜCKLICHER WURF", context),
            RollHistoryOutcome.Fumble => GetSpecialStatusText("PATZER", context),
            _ => "WURF"
        };
    }

    private static RollHistoryOutcome? GetVisualOutcome(RollHistoryEntryDto entry)
    {
        if (entry.Context is { Kind: RollHistoryKind.Attribute, Outcome: RollHistoryOutcome.Success or RollHistoryOutcome.Failure })
        {
            return null;
        }

        return entry.Context?.Outcome;
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

    private static string? GetHeroName(RollHistoryEntryDto entry)
    {
        return string.IsNullOrWhiteSpace(entry.Context?.Snapshot?.HeroName)
            ? null
            : entry.Context.Snapshot.HeroName;
    }

    private static bool IsInitiativeEntry(RollHistoryEntryDto entry) =>
        entry.Context?.Snapshot?.Combat?.Action == CombatActionKind.InitiativeHelper;

    private static bool IsStateChange(RollHistoryEntryDto entry) =>
        entry.Context?.Snapshot?.CombatStateChange is not null;

    private static CombatStateChangeDto? GetCombatStateChange(RollHistoryEntryDto entry) =>
        entry.Context?.Snapshot?.CombatStateChange;

    private static string[] GetCombatSummary(RollHistoryEntryDto entry)
    {
        if (entry.Context?.Snapshot?.Combat is not { } combat || IsInitiativeEntry(entry))
        {
            return [];
        }

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(combat.StatusLabel))
        {
            details.Add(combat.StatusLabel);
        }

        if (combat.Damage is { } damage)
        {
            details.Add($"TP {damage.Total}");
            if (damage.ArmorRating is { } armor)
            {
                details.Add($"RS {armor}");
            }

            if (damage.StructurePoints is { } structurePoints)
            {
                details.Add($"SP {structurePoints}");
            }
        }

        if (combat.Zone is { } zone && zone.WoundZone is { } woundZone)
        {
            details.Add($"Zone {GetWoundZoneLabel(woundZone)}");
        }

        if (combat.WoundApplication is { } wounds)
        {
            if (wounds.LePBefore.HasValue && wounds.LePAfter.HasValue)
            {
                details.Add($"LeP {wounds.LePBefore} → {wounds.LePAfter}");
            }

            if (wounds.AddedWounds > 0)
            {
                details.Add($"Wunden +{wounds.AddedWounds}");
            }

            if (wounds.IsIncapacitated)
            {
                details.Add("handlungsunfähig");
            }
        }

        if (combat.EffectiveTarget is { } target && combat.LabeledRolls.Length == 0)
        {
            details.Add($"Ziel {target}");
        }

        if (combat.Modifiers.Length > 0)
        {
            details.Add(string.Join(" · ", combat.Modifiers.Select(FormatModifier)));
        }

        return details.ToArray();
    }

    private static string FormatModifier(CombatModifierDto modifier) =>
        $"{modifier.Label} {(modifier.Value > 0 ? "+" : string.Empty)}{modifier.Value}";

    private static string GetWoundZoneLabel(CombatWoundZone zone) => zone switch
    {
        CombatWoundZone.Head => "Kopf",
        CombatWoundZone.Torso => "Brust/Rücken",
        CombatWoundZone.Abdomen => "Bauch",
        CombatWoundZone.LeftArm => "linker Arm",
        CombatWoundZone.RightArm => "rechter Arm",
        CombatWoundZone.LeftLeg => "linkes Bein",
        CombatWoundZone.RightLeg => "rechtes Bein",
        _ => zone.ToString()
    };

    private static int GetInitiativeBase(RollHistoryEntryDto entry) =>
        entry.Context?.Snapshot?.Combat?.BaseValue ?? 0;

    private static int GetInitiativeModifier(RollHistoryEntryDto entry)
    {
        var snapshot = entry.Context?.Snapshot?.Combat;
        return snapshot is null
            ? entry.Modifier - GetInitiativeBase(entry)
            : snapshot.Modifiers.Sum(modifier => modifier.Value);
    }

    private static string FormatSigned(int value) => value < 0
        ? $"−{Math.Abs(value)}"
        : $"+{value}";

    private static string GetInitiativeAriaLabel(RollHistoryEntryDto entry)
    {
        var dice = entry.Rolls.Length == 0
            ? "kein Wurf"
            : string.Join(" plus ", entry.Rolls.Select(roll => $"W{roll.Sides} {roll.Value}"));
        return $"{dice} plus {GetInitiativeModifier(entry)} Modifikation plus " +
               $"{GetInitiativeBase(entry)} Initiative-Basiswert ergibt {entry.TotalSum}";
    }

    private static bool ShouldShowCompactEquation(RollHistoryEntryDto entry)
    {
        return entry.Context?.Kind is not (RollHistoryKind.Talent or RollHistoryKind.Spell or RollHistoryKind.Combat);
    }

    private static string GetCompactDifferenceText(RollHistoryCheckDto check)
    {
        return check.Difference > 0 ? $"Überschreitung {check.Difference}" : "0";
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
        var difference = check.Difference > 0 ? $", Überschreitung {check.Difference}" : string.Empty;
        return $"{check.Name}: {check.Roll} von {check.TargetValue}{difference}, {GetCheckStateText(check.State)}";
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
