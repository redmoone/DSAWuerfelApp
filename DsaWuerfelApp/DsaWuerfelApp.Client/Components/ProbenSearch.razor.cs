using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DsaWuerfelApp.Client.Components;

public partial class ProbenSearch
{
    private readonly IReadOnlyList<ProbeSearchEntryDto> _fallbackProben = DefaultProbeCatalog.CreateEntries();

    private bool _isDropdownOpen;
    private string _lastSelectedProbe = string.Empty;
    private ElementReference _searchInput;
    private bool _hasFocus;
    private bool _isResultButtonFocused;
    private bool _suppressNextInputFocusOpen;
    private long _focusGeneration;

    [Parameter] public IReadOnlyList<ProbeSearchEntryDto>? AvailableProben { get; set; }
    [Parameter] public bool UseFallbackCatalog { get; set; } = true;
    [Parameter] public string Placeholder { get; set; } = "Nach Proben suchen...";
    [Parameter] public string SelectedProbe { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> SelectedProbeChanged { get; set; }

    public string SearchTerm { get; set; } = string.Empty;

    private IReadOnlyList<ProbeSearchEntryDto> ProbenSource =>
        AvailableProben is { Count: > 0 }
            ? AvailableProben
            : UseFallbackCatalog
                ? _fallbackProben
                : Array.Empty<ProbeSearchEntryDto>();

    private IEnumerable<ProbeSearchEntryDto> FilteredProben =>
        string.IsNullOrWhiteSpace(SearchTerm)
            ? ProbenSource.Where(probe => probe.IsSelectable)
            : ProbenSource.Where(MatchesSearchTerm);

    protected override void OnParametersSet()
    {
        var resolvedDisplayLabel = ResolveDisplayLabel(SelectedProbe);

        if (!string.Equals(_lastSelectedProbe, SelectedProbe, StringComparison.Ordinal))
        {
            SearchTerm = resolvedDisplayLabel;
            _lastSelectedProbe = SelectedProbe;
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedProbe) &&
            string.Equals(SearchTerm, SelectedProbe, StringComparison.Ordinal) &&
            !string.Equals(SearchTerm, resolvedDisplayLabel, StringComparison.Ordinal))
        {
            SearchTerm = resolvedDisplayLabel;
        }
    }

    private bool MatchesSearchTerm(ProbeSearchEntryDto probe)
    {
        if (probe.DisplayLabel.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (probe.Alternatives.Any(alternative =>
                alternative.Label.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var canonicalSearchTerm = TalentCatalogText.CanonicalizeText(SearchTerm);
        if (string.IsNullOrWhiteSpace(canonicalSearchTerm))
        {
            return false;
        }

        if (TalentCatalogText.CanonicalizeText(probe.DisplayLabel)
            .Contains(canonicalSearchTerm, StringComparison.Ordinal))
        {
            return true;
        }

        return probe.Alternatives.Any(alternative =>
            TalentCatalogText.CanonicalizeText(alternative.Label)
                .Contains(canonicalSearchTerm, StringComparison.Ordinal));
    }

    private Task SelectProbe(string probe)
    {
        var returnFocusToInput = _isResultButtonFocused;

        SelectedProbe = probe;
        SearchTerm = ResolveDisplayLabel(probe);
        _lastSelectedProbe = probe;
        _isDropdownOpen = false;
        _isResultButtonFocused = false;
        return SelectProbeAndRestoreFocusAsync(probe, returnFocusToInput);
    }

    private async Task SelectProbeAndRestoreFocusAsync(string probe, bool returnFocusToInput)
    {
        await SelectedProbeChanged.InvokeAsync(probe);

        if (!returnFocusToInput)
        {
            return;
        }

        _suppressNextInputFocusOpen = true;
        await _searchInput.FocusAsync();
    }

    private Task HandleEntryClick(ProbeSearchEntryDto probe)
    {
        return probe.IsSelectable && !string.IsNullOrWhiteSpace(probe.Value)
            ? SelectProbe(probe.Value)
            : Task.CompletedTask;
    }

    private async Task HandleInputClick()
    {
        _isDropdownOpen = true;

        if (string.IsNullOrWhiteSpace(SelectedProbe))
        {
            return;
        }

        var resolvedDisplayLabel = ResolveDisplayLabel(SelectedProbe);
        var matchesSelectedProbe = string.Equals(SearchTerm, SelectedProbe, StringComparison.Ordinal);
        var matchesDisplayLabel = string.Equals(SearchTerm, resolvedDisplayLabel, StringComparison.Ordinal);

        if (!matchesSelectedProbe && !matchesDisplayLabel)
        {
            return;
        }

        SelectedProbe = string.Empty;
        SearchTerm = string.Empty;
        _lastSelectedProbe = string.Empty;
        await SelectedProbeChanged.InvokeAsync(string.Empty);
    }

    private void HandleInputFocus(FocusEventArgs args)
    {
        _isResultButtonFocused = false;

        if (_suppressNextInputFocusOpen)
        {
            _suppressNextInputFocusOpen = false;
            _isDropdownOpen = false;
            return;
        }

        _isDropdownOpen = true;
    }

    private void HandleResultFocus(FocusEventArgs args)
    {
        _isResultButtonFocused = true;
    }

    private void HandleFocusIn(FocusEventArgs args)
    {
        _hasFocus = true;
        _focusGeneration++;
    }

    private Task HandleFocusOut(FocusEventArgs args)
    {
        _hasFocus = false;
        return HandleBlur();
    }

    private async Task HandleBlur()
    {
        var generation = ++_focusGeneration;
        await Task.Delay(150);

        if (_hasFocus || generation != _focusGeneration)
        {
            return;
        }

        _isDropdownOpen = false;
    }

    private string ResolveDisplayLabel(string selectedProbe)
    {
        if (string.IsNullOrWhiteSpace(selectedProbe))
        {
            return string.Empty;
        }

        var parsedSelection = ProbeSelectionValue.Parse(selectedProbe);

        var probe = ProbenSource.FirstOrDefault(entry =>
            string.Equals(entry.Value, selectedProbe, StringComparison.Ordinal));
        if (probe is not null)
        {
            return probe.DisplayLabel;
        }

        var alternative = ProbenSource
            .SelectMany(entry => entry.Alternatives)
            .FirstOrDefault(entry => string.Equals(entry.Value, selectedProbe, StringComparison.Ordinal));

        if (alternative is not null)
        {
            return parsedSelection.HasOption ? parsedSelection.DisplayName : alternative.Label;
        }

        return parsedSelection.Kind is ProbeSelectionKind.Spell or ProbeSelectionKind.Talent
            ? parsedSelection.DisplayName
            : selectedProbe;
    }
}
