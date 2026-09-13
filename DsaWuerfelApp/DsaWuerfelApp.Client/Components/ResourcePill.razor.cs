using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class ResourcePill
{
    [Parameter] public string Label { get; set; } = "WERT";
    [Parameter] public int? Value { get; set; }
    [Parameter] public int? Maximum { get; set; }
    [Parameter] public int? Minimum { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public EventCallback<int?> ValueChanged { get; set; }
    [Parameter] public string? Class { get; set; }
}
