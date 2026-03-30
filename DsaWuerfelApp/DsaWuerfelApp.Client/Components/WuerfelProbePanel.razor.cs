using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class WuerfelProbePanel
{
    [Parameter]
    public IReadOnlyList<ProbeSearchEntryDto> AvailableProben { get; set; } = Array.Empty<ProbeSearchEntryDto>();

    [Parameter] public string Placeholder { get; set; } = "Nach Proben suchen...";
    [Parameter] public string SelectedProbe { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> SelectedProbeChanged { get; set; }
    [Parameter] public EventCallback InfoRequested { get; set; }
    [Parameter] public bool CanShowInfo { get; set; }
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
}