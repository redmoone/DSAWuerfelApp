using Microsoft.AspNetCore.Components;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Components;

public partial class RollHistory : IDisposable
{
    private List<RollResult> _history = new();

    protected override void OnInitialized()
    {
        // Komponente lauscht selbst auf Multiplayer-Würfe
        GameClient.OnRollResultReceived += HandleIncomingRoll;
    }

    public void Dispose()
    {
        GameClient.OnRollResultReceived -= HandleIncomingRoll;
    }

    private void HandleIncomingRoll(RollResult result)
    {
        InvokeAsync(() =>
        {
            _history.Insert(0, result);
            StateHasChanged();
        });
    }

    // Erlaubt der Wuerfel-Page, lokale Offline-Würfe in das Log zu pushen
    public void AddLocalRoll(RollResult result)
    {
        _history.Insert(0, result);
        StateHasChanged();
    }
}