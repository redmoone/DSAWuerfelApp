using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class TextPill
{
    [Parameter] public string Value { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> ValueChanged { get; set; }
    [Parameter] public string Label { get; set; } = "TEXT";
    [Parameter] public string Placeholder { get; set; } = "Text eingeben";
    [Parameter] public bool Disabled { get; set; }

    private Task HandleInput(ChangeEventArgs args)
    {
        return ValueChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty);
    }
}