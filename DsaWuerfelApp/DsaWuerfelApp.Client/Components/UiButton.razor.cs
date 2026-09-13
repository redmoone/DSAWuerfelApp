using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DsaWuerfelApp.Client.Components;

public partial class UiButton
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
    [Parameter] public EventCallback<MouseEventArgs> ContextMenuRequested { get; set; }
    [Parameter] public bool PreventContextMenu { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public bool Active { get; set; }
    [Parameter] public string Type { get; set; } = "button";
    [Parameter] public string Variant { get; set; } = "pill";
    [Parameter] public string Size { get; set; } = "regular";
    [Parameter] public string? Class { get; set; }
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private ElementReference _element;

    private string CssClass => string.Join(' ', new[]
    {
        "ui-button",
        $"ui-button--{Variant}",
        $"ui-button--{Size}",
        Active ? "is-active" : null,
        Class
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private Task HandleContextMenu(MouseEventArgs args) => ContextMenuRequested.InvokeAsync(args);

    public ValueTask FocusAsync() => _element.FocusAsync();
}
