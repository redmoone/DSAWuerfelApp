using DsaWuerfelApp.Client.Services;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Lobby
{
    private string _error = "";
    private string _joinCode = "";
    private string _userName = "";
    private SessionMode CurrentMode { get; set; } = SessionMode.Join;

    [Inject] public GameClient Game { get; set; } = null!;
    [Inject] public NavigationManager Nav { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await Game.StartAsync();
    }

    private async Task Join()
    {
        if (string.IsNullOrWhiteSpace(_userName))
        {
            _error = "Bitte Namen eingeben";
            return;
        }

        if (string.IsNullOrWhiteSpace(_joinCode))
        {
            _error = "Bitte Session-Code eingeben";
            return;
        }

        var success = await Game.JoinSession(_joinCode.ToUpper(), _userName);
        if (success)
        {
            Nav.NavigateTo("/wuerfel");
        }
        else
        {
            _error = "Session nicht gefunden oder Code falsch.";
        }
    }

    private async Task Create()
    {
        if (string.IsNullOrWhiteSpace(_userName))
        {
            _error = "Bitte Namen eingeben";
            return;
        }

        _ = await Game.CreateSession(_userName);
        Nav.NavigateTo("/wuerfel");
    }

    private void SetMode(SessionMode mode)
    {
        CurrentMode = mode;
        _error = string.Empty;
    }

    private string GetModeClass(SessionMode mode)
    {
        return CurrentMode == mode ? "active" : string.Empty;
    }

    private enum SessionMode
    {
        Join,
        Create
    }
}