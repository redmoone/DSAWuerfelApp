using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class AttributePanel
{
    [Parameter] public IReadOnlyList<string> SelectedAttributes { get; set; } = [];
    [Parameter] public EventCallback<string> OnAttributeToggled { get; set; }

    private int GetCount(string shortName) => SelectedAttributes.Count(a => a == shortName);

    private Task Toggle(string shortName) => OnAttributeToggled.InvokeAsync(shortName);
}