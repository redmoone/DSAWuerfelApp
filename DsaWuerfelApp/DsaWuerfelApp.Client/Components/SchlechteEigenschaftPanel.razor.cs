using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class SchlechteEigenschaftPanel
{
    [Parameter] public bool HasActiveHero { get; set; }
    [Parameter] public IReadOnlyList<BadTraitDto> SchlechteEigenschaften { get; set; } = Array.Empty<BadTraitDto>();
    [Parameter] public string? SelectedName { get; set; }
    [Parameter] public EventCallback<string?> SelectedNameChanged { get; set; }
    [Parameter] public EventCallback OnDirectRollRequested { get; set; }
    [Parameter] public bool IsBusy { get; set; }

    private IReadOnlyList<BadTraitDto> SortedSchlechteEigenschaften =>
        SchlechteEigenschaften
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

    private BadTraitDto? SelectedEigenschaft =>
        SortedSchlechteEigenschaften.FirstOrDefault(entry =>
            string.Equals(entry.Name, SelectedName, StringComparison.Ordinal));

    private bool IsSelected(string name) => string.Equals(SelectedName, name, StringComparison.Ordinal);

    private Task HandleDirectRollRequested() => OnDirectRollRequested.InvokeAsync();

    private async Task HandleSelectionChanged(string name)
    {
        var nextSelection = IsSelected(name) ? null : name;
        await SelectedNameChanged.InvokeAsync(nextSelection);
    }

    private static string FormatModifier(int modifier)
    {
        return modifier > 0 ? $"+{modifier}" : modifier.ToString();
    }
}