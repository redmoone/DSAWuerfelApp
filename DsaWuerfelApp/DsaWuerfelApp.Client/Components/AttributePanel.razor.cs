using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class AttributePanel
{
    [Parameter] public IReadOnlyList<string> SelectedAttributes { get; set; } = [];
    [Parameter] public EventCallback<string> OnAttributeAdded { get; set; }
    [Parameter] public EventCallback<string> OnAttributeRemoved { get; set; }

    private int GetCount(string shortName) => SelectedAttributes.Count(a => a == shortName);

    private Task HandleIncrease(string shortName) => OnAttributeAdded.InvokeAsync(shortName);
    private Task HandleDecrease(string shortName) => OnAttributeRemoved.InvokeAsync(shortName);
}