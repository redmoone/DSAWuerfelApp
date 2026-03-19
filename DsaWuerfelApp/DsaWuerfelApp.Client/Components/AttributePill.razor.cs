using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class AttributePill
{
    [Parameter] public string ShortName { get; set; } = string.Empty;
    [Parameter] public int Value { get; set; }
    [Parameter] public string IconPath { get; set; } = string.Empty;
    [Parameter] public int SelectionCount { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }

    private Task OnClickCallback()
        => OnClick.InvokeAsync();
}