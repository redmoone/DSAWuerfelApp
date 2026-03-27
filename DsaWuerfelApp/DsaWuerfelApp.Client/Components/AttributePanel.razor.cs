using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class AttributePanel
{
    [Parameter] public IReadOnlyDictionary<string, int> AttributeValues { get; set; } = new Dictionary<string, int>();
    [Parameter] public IReadOnlyList<string> SelectedAttributes { get; set; } = [];
    [Parameter] public EventCallback<string> OnAttributeAdded { get; set; }
    [Parameter] public EventCallback<string> OnAttributeRemoved { get; set; }

    private int GetCount(string shortName) => SelectedAttributes.Count(a => a == shortName);

    private int GetValue(string shortName, int fallbackValue) =>
        AttributeValues.TryGetValue(shortName, out var value) ? value : fallbackValue;

    private Task HandleIncrease(string shortName) => OnAttributeAdded.InvokeAsync(shortName);
    private Task HandleDecrease(string shortName) => OnAttributeRemoved.InvokeAsync(shortName);
}