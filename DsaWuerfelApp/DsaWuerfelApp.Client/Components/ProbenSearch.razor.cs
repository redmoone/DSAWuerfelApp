using DsaWuerfelApp.Client.Services;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public sealed record ProbeSearchAlternative(string Label, string Value);

public sealed record ProbeSearchEntry(
    string DisplayLabel,
    string? Value,
    bool IsSelectable,
    IReadOnlyList<ProbeSearchAlternative> Alternatives);

public partial class ProbenSearch
{
    private readonly List<ProbeSearchEntry> _fallbackProben =
    [
        new("Klettern (MU/GE/KK)", "Klettern (MU/GE/KK)", true, []),
        new("Koerperbeherrschung (GE/GE/KO)", "Koerperbeherrschung (GE/GE/KO)", true, []),
        new("Sinnesschaerfe (KL/IN/IN)", "Sinnesschaerfe (KL/IN/IN)", true, []),
        new("Ueberreden (MU/IN/CH)", "Ueberreden (MU/IN/CH)", true, []),
        new("Verbergen (MU/IN/AG)", "Verbergen (MU/IN/AG)", true, [])
    ];

    private bool _isDropdownOpen;
    private string _lastSelectedProbe = string.Empty;

    [Parameter] public IReadOnlyList<ProbeSearchEntry>? AvailableProben { get; set; }
    [Parameter] public string Placeholder { get; set; } = "Nach Proben suchen...";
    [Parameter] public string SelectedProbe { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> SelectedProbeChanged { get; set; }

    public string SearchTerm { get; set; } = string.Empty;

    private IReadOnlyList<ProbeSearchEntry> ProbenSource =>
        AvailableProben is { Count: > 0 } ? AvailableProben : _fallbackProben;

    private IEnumerable<ProbeSearchEntry> FilteredProben =>
        string.IsNullOrWhiteSpace(SearchTerm)
            ? ProbenSource.Where(probe => probe.IsSelectable)
            : ProbenSource.Where(MatchesSearchTerm);

    protected override void OnParametersSet()
    {
        if (string.Equals(_lastSelectedProbe, SelectedProbe, StringComparison.Ordinal))
        {
            return;
        }

        SearchTerm = SelectedProbe;
        _lastSelectedProbe = SelectedProbe;
    }

    private bool MatchesSearchTerm(ProbeSearchEntry probe)
    {
        if (probe.DisplayLabel.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var canonicalSearchTerm = TalentCatalog.CanonicalizeName(SearchTerm);
        if (string.IsNullOrWhiteSpace(canonicalSearchTerm))
        {
            return false;
        }

        if (TalentCatalog.CanonicalizeName(probe.DisplayLabel).Contains(canonicalSearchTerm, StringComparison.Ordinal))
        {
            return true;
        }

        return probe.Alternatives.Any(alternative =>
            TalentCatalog.CanonicalizeName(alternative.Label).Contains(canonicalSearchTerm, StringComparison.Ordinal));
    }

    private Task SelectProbe(string probe)
    {
        SelectedProbe = probe;
        SearchTerm = probe;
        _lastSelectedProbe = probe;
        _isDropdownOpen = false;
        return SelectedProbeChanged.InvokeAsync(probe);
    }

    private Task HandleEntryClick(ProbeSearchEntry probe)
    {
        return probe.IsSelectable && !string.IsNullOrWhiteSpace(probe.Value)
            ? SelectProbe(probe.Value)
            : Task.CompletedTask;
    }

    public async Task ClearAsync()
    {
        SearchTerm = string.Empty;
        SelectedProbe = string.Empty;
        _lastSelectedProbe = string.Empty;
        _isDropdownOpen = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task HandleInputClick()
    {
        _isDropdownOpen = true;

        if (string.IsNullOrWhiteSpace(SelectedProbe) ||
            !string.Equals(SearchTerm, SelectedProbe, StringComparison.Ordinal))
        {
            return;
        }

        await ClearAsync();
        await SelectedProbeChanged.InvokeAsync(string.Empty);
    }

    private async Task HandleBlur()
    {
        await Task.Delay(150);
        _isDropdownOpen = false;
    }
}