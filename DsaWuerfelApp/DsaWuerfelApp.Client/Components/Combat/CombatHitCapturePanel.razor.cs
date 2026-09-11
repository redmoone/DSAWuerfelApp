using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components.Combat;

public partial class CombatHitCapturePanel
{
    [Parameter] public CombatWoundZone? SelectedZone { get; set; }
    [Parameter] public EventCallback<CombatWoundZone?> SelectedZoneChanged { get; set; }
    [Parameter] public string LePLossText { get; set; } = "0";
    [Parameter] public EventCallback<string> LePLossTextChanged { get; set; }
    [Parameter] public int NewWounds { get; set; }
    [Parameter] public EventCallback<int> NewWoundsChanged { get; set; }
    [Parameter] public string Note { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> NoteChanged { get; set; }
    [Parameter] public int? CurrentLeP { get; set; }
    [Parameter] public int? PreviewLeP { get; set; }
    [Parameter] public int? CurrentWounds { get; set; }
    [Parameter] public bool CanApply { get; set; }
    [Parameter] public bool IsBusy { get; set; }
    [Parameter] public EventCallback ApplyRequested { get; set; }
    [Parameter] public EventCallback CancelRequested { get; set; }

    private async Task HandleZoneChanged(ChangeEventArgs args)
    {
        if (Enum.TryParse<CombatWoundZone>(args.Value?.ToString(), out var zone))
        {
            await SelectedZoneChanged.InvokeAsync(zone);
        }
    }

    private static string GetZoneLabel(CombatWoundZone zone) => zone switch
    {
        CombatWoundZone.Head => "Kopf",
        CombatWoundZone.Torso => "Brust / Rücken",
        CombatWoundZone.Abdomen => "Bauch",
        CombatWoundZone.LeftArm => "Linker Arm",
        CombatWoundZone.RightArm => "Rechter Arm",
        CombatWoundZone.LeftLeg => "Linkes Bein",
        CombatWoundZone.RightLeg => "Rechtes Bein",
        _ => zone.ToString()
    };

    private static string FormatValue(int? value) => value?.ToString() ?? "nicht gestartet";
}
