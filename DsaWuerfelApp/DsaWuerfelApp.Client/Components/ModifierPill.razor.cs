using Microsoft.AspNetCore.Components;

using Microsoft.AspNetCore.Components.Web;

namespace DsaWuerfelApp.Client.Components;

public partial class ModifierPill
{
    [Parameter] public int Value { get; set; }
    [Parameter] public int? DisplayValue { get; set; }
    [Parameter] public string? LabelOverride { get; set; }
    [Parameter] public bool AllowDirectInput { get; set; }

    [Parameter] public EventCallback<int> ValueChanged { get; set; }

    private int CurrentDisplayValue => DisplayValue ?? Value;

    private string IconPath => CurrentDisplayValue <= 0 ? "feather.svg" : "weight.svg";
    private string LabelText => LabelOverride ?? (CurrentDisplayValue <= 0 ? "ERLEICH." : "ERSCHW.");

    private string FormattedValue =>
        CurrentDisplayValue > 0 ? $"+{CurrentDisplayValue}" : CurrentDisplayValue.ToString();

    private string _draft = string.Empty;
    private int _lastValue;
    private bool _editing;

    protected override void OnParametersSet()
    {
        if (!_editing || _lastValue != Value)
        {
            _draft = Value.ToString();
            _lastValue = Value;
            _editing = false;
        }
    }

    private Task Decrease() => ValueChanged.InvokeAsync(Value - 1);
    private Task Increase() => ValueChanged.InvokeAsync(Value + 1);

    private void HandleInput(ChangeEventArgs args)
    {
        _editing = true;
        _draft = args.Value?.ToString() ?? string.Empty;
    }

    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            _draft = Value.ToString();
            _editing = false;
        }
        else if (args.Key == "Enter")
        {
            await CommitAsync();
        }
    }

    private async Task CommitAsync()
    {
        if (!int.TryParse(_draft, out var value))
        {
            _draft = Value.ToString();
            _editing = false;
            return;
        }

        value = Math.Clamp(value, -999, 999);
        _draft = value.ToString();
        _editing = false;
        if (value != Value)
        {
            _lastValue = value;
            await ValueChanged.InvokeAsync(value);
        }
    }
}
