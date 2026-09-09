using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class WuerfelProbePanel
{
    [Parameter]
    public IReadOnlyList<ProbeSearchEntryDto> AvailableProben { get; set; } = Array.Empty<ProbeSearchEntryDto>();

    [Parameter] public bool UseFallbackCatalog { get; set; } = true;
    [Parameter] public string Placeholder { get; set; } = "Nach Proben suchen...";
    [Parameter] public string SelectedProbe { get; set; } = string.Empty;
    [Parameter] public ProbeInfoResultDto? ProbeInfo { get; set; }
    [Parameter] public EventCallback<string> SelectedProbeChanged { get; set; }
    [Parameter] public EventCallback<string> SpellOptionToggleRequested { get; set; }
    [Parameter] public EventCallback InfoRequested { get; set; }
    [Parameter] public bool CanShowInfo { get; set; }
    [Parameter] public bool IsInfoExpanded { get; set; }
    [Parameter] public bool ShowDebugForcedRolls { get; set; }
    [Parameter] public string ForcedRollsText { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> ForcedRollsTextChanged { get; set; }
    [Parameter] public EventCallback<string> PresetRequested { get; set; }
    [Parameter] public EventCallback ClearForcedRollsRequested { get; set; }
    [Parameter] public bool IsBusy { get; set; }

    private Task HandleForcedRollsChanged(ChangeEventArgs args)
    {
        return ForcedRollsTextChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty);
    }

    private static bool HasSelectedOption(SpellOptionGroupDto group)
    {
        return group.Options.Any(option => option.IsSelected);
    }

    private static string GetGroupSummary(SpellOptionGroupDto group)
    {
        var selectedCount = group.Options.Count(option => option.IsSelected);
        return $"{selectedCount} von {group.Options.Length} in dieser Gruppe";
    }

    private static string GetGlobalSelectionSummary(SpellSelectionPanelDto selection)
    {
        return selection.MaximumSelectableOptions is { } maximum
            ? $"{selection.SelectedOptionCount} / {maximum} gleichzeitig"
            : $"{selection.SelectedOptionCount} gleichzeitig";
    }

    private static bool IsMaximumDisabled(SpellSelectionPanelDto selection, SpellOptionButtonDto option)
    {
        return option.IsDisabled &&
               !option.IsSelected &&
               selection.MaximumSelectableOptions is { } maximum &&
               selection.SelectedOptionCount >= maximum;
    }

    private static string GetMaximumHintId(int groupIndex, int optionIndex)
    {
        return $"spell-option-maximum-{groupIndex}-{optionIndex}";
    }

    private string BuildInfoButtonStyle()
    {
        var backgroundColor = IsInfoExpanded ? "var(--dsa-gold)" : "var(--pill-bg)";
        var foregroundColor = IsInfoExpanded ? "var(--dsa-bg)" : "var(--dsa-white)";
        return
            $"width: 48px; height: 48px; background-color: {backgroundColor}; color: {foregroundColor}; border: 2px solid var(--dsa-gold);";
    }
}
