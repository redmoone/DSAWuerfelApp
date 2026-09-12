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
    [Parameter] public CombatSetVariantDto? SelectedSet { get; set; }
    [Parameter] public IReadOnlyList<CombatSetVariantDto> Sets { get; set; } = Array.Empty<CombatSetVariantDto>();
    [Parameter] public string? SelectedSetId { get; set; }
    [Parameter] public EventCallback<string> SetSelected { get; set; }
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
    [Parameter] public EventCallback<ResourceAdjustment> RelativeResourceChangedRequested { get; set; }
    [Parameter] public EventCallback InitiativeRequested { get; set; }
    [Parameter] public EventCallback<int?> InitiativeChangedRequested { get; set; }
    [Parameter] public EventCallback InitiativeRollRequested { get; set; }
    [Parameter] public bool CanRollInitiative { get; set; }
    [Parameter] public string? InitiativeDisabledReason { get; set; }
    [Parameter] public EventCallback WoundsRequested { get; set; }
    [Parameter] public bool ShowUndo { get; set; } = true;
    [Parameter] public bool CanUndo { get; set; }
    [Parameter] public EventCallback UndoRequested { get; set; }
    [Parameter] public string? PersistenceWarning { get; set; }
    [Parameter] public IReadOnlyDictionary<CombatWoundZone, int?> Wounds { get; set; } =
        new Dictionary<CombatWoundZone, int?>();
    [Parameter] public IReadOnlyList<string> Effects { get; set; } = Array.Empty<string>();
    [Parameter] public bool InlineEditing { get; set; }
    [Parameter] public bool IsMutationBusy { get; set; }

    private CombatResourceKind? _relativeResource;
    private int? _relativeAmount = 1;

    private int TotalWounds => Wounds.Values.Where(value => value.HasValue).Sum(value => Math.Clamp(value!.Value, 0, 3));
    private bool HasUnknownWounds => Wounds.Values.Any(value => !value.HasValue);
    private bool HasUnknownRequiredState => HasUnknownWounds ||
                                            ShouldShowResource(CombatResourceKind.LeP, Profile?.Resources.LeP) && !CurrentLeP.HasValue ||
                                            ShouldShowResource(CombatResourceKind.AuP, Profile?.Resources.AuP) && !CurrentAuP.HasValue ||
                                            ShouldShowResource(CombatResourceKind.AeP, Profile?.Resources.AeP) && !CurrentAeP.HasValue ||
                                            ShouldShowResource(CombatResourceKind.KeP, Profile?.Resources.KeP) && !CurrentKeP.HasValue;
    private string WoundSummary => HasUnknownWounds
        ? TotalWounds > 0 ? $"{TotalWounds} · nicht vollständig erfasst" : "Nicht erfasst"
        : $"{TotalWounds} · Körperzonen";
    private int? DisplayedInitiative => CurrentInitiative ?? SelectedSet?.Initiative;

    private static bool ShouldShowResource(CombatResourceKind resource, int? maximum) =>
        resource == CombatResourceKind.LeP ? !maximum.HasValue || maximum.Value > 0 : maximum is > 0;

    private static string FormatResource(int? current, int? maximum)
    {
        if (!current.HasValue)
        {
            return maximum.HasValue ? $"— / {maximum.Value}" : "—";
        }

        return maximum.HasValue ? $"{current.Value} / {maximum.Value}" : current.Value.ToString();
    }

    private static string FormatMaximum(int? maximum) => maximum?.ToString() ?? "—";

    private static string GetBarWidth(int? current, int? maximum)
    {
        if (!current.HasValue || !maximum.HasValue || maximum.Value <= 0)
        {
            return "0%";
        }

        var ratio = Math.Clamp((double)current.Value / maximum.Value, 0d, 1d);
        return $"{ratio:P0}";
    }

    private static string FormatValue(int? value) => value?.ToString() ?? "—";

    private string MissingStateSummary
    {
        get
        {
            var missing = new List<string>();
            if (ShouldShowResource(CombatResourceKind.LeP, Profile?.Resources.LeP) && !CurrentLeP.HasValue) missing.Add("LeP");
            if (ShouldShowResource(CombatResourceKind.AuP, Profile?.Resources.AuP) && !CurrentAuP.HasValue) missing.Add("AuP");
            if (ShouldShowResource(CombatResourceKind.AeP, Profile?.Resources.AeP) && !CurrentAeP.HasValue) missing.Add("AeP");
            if (ShouldShowResource(CombatResourceKind.KeP, Profile?.Resources.KeP) && !CurrentKeP.HasValue) missing.Add("KE");
            if (HasUnknownWounds) missing.Add("Wunden");
            return missing.Count == 0 ? "Aktuelle Werte noch nicht erfasst" : $"Nicht erfasst: {string.Join(", ", missing)}";
        }
    }

    private static int? GetResourceMinimum(CombatResourceKind resource) => resource == CombatResourceKind.LeP ? null : 0;

    private static string GetSetLabel(CombatSetVariantDto set)
    {
        var model = set.ArmorModel switch
        {
            CombatArmorModel.Zone => "Zonenrüstung",
            CombatArmorModel.Simple => "Einfache Rüstung",
            _ => "Rüstungsmodell unbekannt"
        };
        return $"Set {set.Number} · {model}";
    }

    private string GetArmorLabel() => SelectedSet?.ArmorModel switch
    {
        CombatArmorModel.Zone => "Zonenrüstung",
        CombatArmorModel.Simple => "Einfache Rüstung",
        _ => "Rüstungsdaten fehlen"
    };

    private void ToggleRelativeAmount(CombatResourceKind resource)
    {
        if (IsMutationBusy)
        {
            return;
        }

        _relativeResource = _relativeResource == resource ? null : resource;
        _relativeAmount = 1;
    }

    private Task HandleRelativeAmountChanged(int? value)
    {
        _relativeAmount = value.HasValue ? Math.Max(1, value.Value) : null;
        return Task.CompletedTask;
    }

    private async Task ApplyRelativeAsync(bool increase)
    {
        if (_relativeResource is not { } resource || !_relativeAmount.HasValue || _relativeAmount.Value <= 0 || GetResourceValue(resource) is null)
        {
            return;
        }

        await RelativeResourceChangedRequested.InvokeAsync(new ResourceAdjustment(resource, _relativeAmount.Value, increase));
        _relativeResource = null;
    }

    private int? GetResourceValue(CombatResourceKind resource) => resource switch
    {
        CombatResourceKind.LeP => CurrentLeP,
        CombatResourceKind.AuP => CurrentAuP,
        CombatResourceKind.AeP => CurrentAeP,
        CombatResourceKind.KeP => CurrentKeP,
        _ => null
    };

    private Task HandleSetChanged(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        return string.IsNullOrWhiteSpace(value) ? Task.CompletedTask : SetSelected.InvokeAsync(value);
    }

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
    public sealed record ResourceAdjustment(CombatResourceKind Resource, int Amount, bool Increase);
}
