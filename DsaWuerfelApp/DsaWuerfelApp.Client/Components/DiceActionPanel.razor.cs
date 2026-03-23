using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class DiceActionPanel
{
    [Parameter] public RenderFragment? ResultPillContent { get; set; }

    [Parameter] public int Modifier { get; set; }

    [Parameter] public EventCallback<int> ModifierChanged { get; set; }

    [Parameter] public bool IsHiddenRoll { get; set; }

    [Parameter] public EventCallback<bool> IsHiddenRollChanged { get; set; }

    [Parameter] public bool HasSelection { get; set; }

    [Parameter] public EventCallback OnRoll { get; set; }

    private Task ToggleHiddenRoll()
    {
        IsHiddenRoll = !IsHiddenRoll;
        return IsHiddenRollChanged.InvokeAsync(IsHiddenRoll);
    }
}