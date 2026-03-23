using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class ModifierPill
{
    [Parameter] public int Value { get; set; }

    [Parameter] public EventCallback<int> ValueChanged { get; set; }

    private string IconPath => Value <= 0 ? "feather.svg" : "weight.svg";
    private string LabelText => Value <= 0 ? "ERLEICH." : "ERSCHW.";
    private string FormattedValue => Value > 0 ? $"+{Value}" : Value.ToString();

    private Task Decrease() => ValueChanged.InvokeAsync(Value - 1);
    private Task Increase() => ValueChanged.InvokeAsync(Value + 1);
}