using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DsaWuerfelApp.Client.Components;

public partial class NumberControl
{
    [Parameter] public int? Value { get; set; }
    [Parameter] public EventCallback<int?> ValueChanged { get; set; }
    [Parameter] public string Label { get; set; } = "WERT";
    [Parameter] public string Placeholder { get; set; } = "—";
    [Parameter] public int? Minimum { get; set; }
    [Parameter] public int? Maximum { get; set; }
    [Parameter] public int? StepMaximum { get; set; }
    [Parameter] public bool AllowEmpty { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public bool DisableStepsWhenEmpty { get; set; }
    [Parameter] public bool ShowValidationMessage { get; set; }

    private string _draft = string.Empty;
    private string? _validationMessage;
    private int? _lastValue;
    private bool _editing;

    private bool IsDecrementDisabled => Disabled || (DisableStepsWhenEmpty && !Value.HasValue);
    private bool IsIncrementDisabled => Disabled || (DisableStepsWhenEmpty && !Value.HasValue);

    protected override void OnParametersSet()
    {
        if (!_editing || _lastValue != Value)
        {
            _draft = Value?.ToString() ?? string.Empty;
            _lastValue = Value;
            _editing = false;
            _validationMessage = null;
        }
    }

    private void HandleInput(ChangeEventArgs args)
    {
        _editing = true;
        _draft = args.Value?.ToString() ?? string.Empty;
    }

    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            _draft = Value?.ToString() ?? string.Empty;
            _editing = false;
            _validationMessage = null;
            return;
        }

        if (args.Key == "Enter")
        {
            await CommitAsync();
        }
    }

    private async Task CommitAsync()
    {
        if (Disabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_draft))
        {
            if (AllowEmpty)
            {
                _editing = false;
                _validationMessage = null;
                if (Value.HasValue)
                {
                    _lastValue = null;
                    await ValueChanged.InvokeAsync(null);
                }

                return;
            }

            _editing = false;
            _validationMessage = "Wert erforderlich.";
            return;
        }

        if (!int.TryParse(_draft, out var parsed))
        {
            _editing = false;
            _validationMessage = "Gültige Zahl eingeben.";
            return;
        }

        var normalized = Minimum.HasValue ? Math.Max(Minimum.Value, parsed) : parsed;
        normalized = Maximum.HasValue ? Math.Min(Maximum.Value, normalized) : normalized;
        _draft = normalized.ToString();
        _editing = false;
        _validationMessage = null;
        if (Value != normalized)
        {
            _lastValue = normalized;
            await ValueChanged.InvokeAsync(normalized);
        }
    }

    private async Task StepAsync(int delta)
    {
        if (Disabled)
        {
            return;
        }

        var current = Value ?? 0;
        var next = current + delta;
        if (Minimum.HasValue)
        {
            next = Math.Max(Minimum.Value, next);
        }

        var stepMaximum = StepMaximum ?? Maximum;
        if (stepMaximum.HasValue)
        {
            next = Math.Min(stepMaximum.Value, next);
        }

        _draft = next.ToString();
        _editing = false;
        _validationMessage = null;
        if (Value != next)
        {
            _lastValue = next;
            await ValueChanged.InvokeAsync(next);
        }
    }
}
