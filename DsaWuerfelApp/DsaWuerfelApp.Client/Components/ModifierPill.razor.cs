using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class ModifierPill
{
    [Parameter] public int Value { get; set; }
    [Parameter] public int? DisplayValue { get; set; }

    [Parameter] public EventCallback<int> ValueChanged { get; set; }

    private int CurrentDisplayValue => DisplayValue ?? Value;

    private string IconPath => CurrentDisplayValue <= 0 ? "feather.svg" : "weight.svg";
    private string LabelText => CurrentDisplayValue <= 0 ? "ERLEICH." : "ERSCHW.";

    private string FormattedValue =>
        CurrentDisplayValue > 0 ? $"+{CurrentDisplayValue}" : CurrentDisplayValue.ToString();

    private Task Decrease() => ValueChanged.InvokeAsync(Value - 1);
    private Task Increase() => ValueChanged.InvokeAsync(Value + 1);
}