using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class ProbenSearch
{
    private readonly List<string> _fallbackProben =
    [
        "Klettern (MU/GE/KK)",
        "Körperbeherrschung (GE/GE/KO)",
        "Sinnesschärfe (KL/IN/IN)",
        "Überreden (MU/IN/CH)",
        "Verbergen (MU/IN/AG)"
    ];

    private bool _isDropdownOpen;

    [Parameter] public IReadOnlyList<string>? AvailableProben { get; set; }
    [Parameter] public string Placeholder { get; set; } = "Nach Proben suchen...";
    [Parameter] public string SelectedProbe { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> SelectedProbeChanged { get; set; }

    public string SearchTerm { get; set; } = string.Empty;

    private IReadOnlyList<string> ProbenSource =>
        AvailableProben is { Count: > 0 } ? AvailableProben : _fallbackProben;

    private IEnumerable<string> FilteredProben =>
        string.IsNullOrWhiteSpace(SearchTerm)
            ? ProbenSource
            : ProbenSource.Where(p => p.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase));

    private Task SelectProbe(string probe)
    {
        SelectedProbe = probe;
        SearchTerm = probe;
        _isDropdownOpen = false;
        return SelectedProbeChanged.InvokeAsync(probe);
    }

    private async Task HandleInputClick()
    {
        _isDropdownOpen = true;

        if (string.IsNullOrWhiteSpace(SelectedProbe) ||
            !string.Equals(SearchTerm, SelectedProbe, StringComparison.Ordinal))
        {
            return;
        }

        SearchTerm = string.Empty;
        SelectedProbe = string.Empty;
        await SelectedProbeChanged.InvokeAsync(string.Empty);
    }

    private async Task HandleBlur()
    {
        await Task.Delay(150);
        _isDropdownOpen = false;
    }
}