using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class ProbenSearch
{
    private readonly List<string> _mockProben =
    [
        "Klettern (MU/GE/KK)",
        "Körperbeherrschung (GE/GE/KO)",
        "Sinnesschärfe (KL/IN/IN)",
        "Überreden (MU/IN/CH)",
        "Verbergen (MU/IN/AG)"
    ];

    private bool _isDropdownOpen;
    [Parameter] public string SelectedProbe { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> SelectedProbeChanged { get; set; }

    public string SearchTerm { get; set; } = string.Empty;

    private IEnumerable<string> FilteredProben =>
        string.IsNullOrWhiteSpace(SearchTerm)
            ? _mockProben
            : _mockProben.Where(p => p.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase));

    private Task SelectProbe(string probe)
    {
        SelectedProbe = probe;
        SearchTerm = probe;
        _isDropdownOpen = false;
        return SelectedProbeChanged.InvokeAsync(probe);
    }

    private async Task HandleBlur()
    {
        await Task.Delay(150);
        _isDropdownOpen = false;
    }
}