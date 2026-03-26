using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DsaWuerfelApp.Client.Components;

public partial class AttributePill : ComponentBase
{
    [Parameter] public string ShortName { get; set; } = string.Empty;
    [Parameter] public int Value { get; set; }
    [Parameter] public string IconPath { get; set; } = string.Empty;
    [Parameter] public int SelectionCount { get; set; }
    [Parameter] public EventCallback<string> OnIncrease { get; set; }
    [Parameter] public EventCallback<string> OnDecrease { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }

    private Task HandleClick(MouseEventArgs e)
    {
        return OnIncrease.InvokeAsync(ShortName);
    }

    private Task HandleRightClick(MouseEventArgs e)
    {
        return OnDecrease.InvokeAsync(ShortName);
    }
}