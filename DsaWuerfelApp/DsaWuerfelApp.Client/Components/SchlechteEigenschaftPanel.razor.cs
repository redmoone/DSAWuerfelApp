using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class SchlechteEigenschaftPanel
{
    private static readonly IReadOnlyDictionary<string, int> EmptyValues = new Dictionary<string, int>();

    [Parameter] public bool HasActiveHero { get; set; }
    [Parameter] public IReadOnlyDictionary<string, int> SchlechteEigenschaften { get; set; } = EmptyValues;
    [Parameter] public string? SelectedName { get; set; }
    [Parameter] public EventCallback<string?> SelectedNameChanged { get; set; }
    [Parameter] public EventCallback OnDirectRollRequested { get; set; }

    private IReadOnlyList<KeyValuePair<string, int>> SortedSchlechteEigenschaften =>
        SchlechteEigenschaften
            .OrderBy(entry => entry.Key)
            .ToList();

    private KeyValuePair<string, int>? SelectedEigenschaft =>
        string.IsNullOrWhiteSpace(SelectedName) || !SchlechteEigenschaften.TryGetValue(SelectedName, out var value)
            ? null
            : new KeyValuePair<string, int>(SelectedName, value);

    private bool IsSelected(string name) => string.Equals(SelectedName, name, StringComparison.Ordinal);

    private Task HandleDirectRollRequested() => OnDirectRollRequested.InvokeAsync();

    private Task HandleSelectionChanged(string name)
    {
        var nextSelection = IsSelected(name) ? null : name;
        return SelectedNameChanged.InvokeAsync(nextSelection);
    }

    private static int GetHalvedWert(int value)
    {
        return value <= 0 ? 0 : (value + 1) / 2;
    }

    private static string FormatModifier(int modifier)
    {
        return modifier > 0 ? $"+{modifier}" : modifier.ToString();
    }
}