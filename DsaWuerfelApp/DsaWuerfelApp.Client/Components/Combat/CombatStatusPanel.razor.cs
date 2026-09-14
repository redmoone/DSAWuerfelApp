using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Client.Services;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components.Combat;

public partial class CombatStatusPanel
{
    [Parameter] public string HeroName { get; set; } = "Kein aktiver Held";
    [Parameter] public string? SessionName { get; set; }
    [Parameter] public CombatProfileDto? Profile { get; set; }
    [Parameter] public bool IsLoading { get; set; }
    [Parameter] public string? ErrorMessage { get; set; }
    [Parameter] public int? CurrentLeP { get; set; }
    [Parameter] public int? CurrentAuP { get; set; }
    [Parameter] public int? CurrentAeP { get; set; }
    [Parameter] public int? CurrentKeP { get; set; }
    [Parameter] public int? CurrentInitiative { get; set; }
    [Parameter] public bool IsCombatStarted { get; set; }
    [Parameter] public bool CanStartCombat { get; set; }
    [Parameter] public EventCallback StartCombatRequested { get; set; }
    [Parameter] public EventCallback<CombatResourceKind> ResourceEditRequested { get; set; }
    [Parameter] public EventCallback<ResourceValueChange> ResourceValueChangedRequested { get; set; }
    [Parameter] public EventCallback<int?> InitiativeValueChangedRequested { get; set; }
    [Parameter] public EventCallback WoundsRequested { get; set; }
    [Parameter] public bool ShowUndo { get; set; } = true;
    [Parameter] public bool CanUndo { get; set; }
    [Parameter] public EventCallback UndoRequested { get; set; }
    [Parameter] public string? PersistenceWarning { get; set; }
    [Parameter] public IReadOnlyDictionary<CombatWoundZone, int?> Wounds { get; set; } =
        new Dictionary<CombatWoundZone, int?>();
    [Parameter] public IReadOnlyList<string> Effects { get; set; } = Array.Empty<string>();
    [Parameter] public bool IsMutationBusy { get; set; }

    private int TotalWounds => Wounds.Values.Where(value => value.HasValue).Sum(value => Math.Clamp(value!.Value, 0, 3));
    private bool HasUnknownWounds => Wounds.Values.Any(value => !value.HasValue);
    private string WoundSummary => HasUnknownWounds
        ? TotalWounds > 0 ? $"{TotalWounds} · nicht vollständig erfasst" : "Nicht erfasst"
        : $"{TotalWounds} · Körperzonen";

    private static bool ShouldShowResource(CombatResourceKind resource, int? maximum) =>
        resource == CombatResourceKind.LeP ? !maximum.HasValue || maximum.Value > 0 : maximum is > 0;

    private static string FormatValue(int? value) => value?.ToString() ?? "—";

    private Task HandleCaptureRequested()
    {
        var resource = FirstMissingResource();
        return resource.HasValue
            ? ResourceEditRequested.InvokeAsync(resource.Value)
            : WoundsRequested.InvokeAsync();
    }

    private CombatResourceKind? FirstMissingResource()
    {
        if (ShouldShowResource(CombatResourceKind.LeP, Profile?.Resources.LeP) && !CurrentLeP.HasValue) return CombatResourceKind.LeP;
        if (ShouldShowResource(CombatResourceKind.AuP, Profile?.Resources.AuP) && !CurrentAuP.HasValue) return CombatResourceKind.AuP;
        if (ShouldShowResource(CombatResourceKind.AeP, Profile?.Resources.AeP) && !CurrentAeP.HasValue) return CombatResourceKind.AeP;
        if (ShouldShowResource(CombatResourceKind.KeP, Profile?.Resources.KeP) && !CurrentKeP.HasValue) return CombatResourceKind.KeP;
        return null;
    }

    public sealed record ResourceValueChange(CombatResourceKind Resource, int? Value);
}
