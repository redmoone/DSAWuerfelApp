using DsaWuerfelApp.Client.Services;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace DsaWuerfelApp.Client.Pages;

public partial class Kampf
{
    private readonly List<string> _availableManeuvers = new()
    {
        "Befreiungsschlag",
        "Entwaffnen",
        "Finte",
        "Gezielter Stich",
        "Hammerschlag",
        "Meisterparade",
        "Niederwerfen",
        "Schildschlag",
        "Sturmangriff",
        "Umreißen",
        "Wuchtschlag"
    };

    private bool _isDropdownOpen;
    [Inject] public GameClient GameClient { get; set; } = null!;
    [Inject] public ISnackbar Snackbar { get; set; } = null!;

    public string SearchTerm { get; set; } = string.Empty;
    public string SelectedManeuver { get; set; } = string.Empty;

    private IEnumerable<string> FilteredManeuvers =>
        string.IsNullOrWhiteSpace(SearchTerm)
            ? _availableManeuvers
            : _availableManeuvers.Where(m => m.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase));

    private void SelectManeuver(string maneuver)
    {
        SelectedManeuver = maneuver;
        SearchTerm = maneuver;
        _isDropdownOpen = false;
    }

    private async Task HandleBlur()
    {
        await Task.Delay(150);
        _isDropdownOpen = false;
    }

    private async Task RollAttribute(string attributeName, int value)
    {
        // Placeholder for the actual rolling logic
        Snackbar.Add($"Wurf für {attributeName} ({value})", Severity.Info);
        await Task.CompletedTask;
    }
}