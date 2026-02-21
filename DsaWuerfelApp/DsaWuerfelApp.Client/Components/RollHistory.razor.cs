using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Components;

public partial class RollHistory : IDisposable
{
    private List<RollResult> _history = [];

    protected override void OnInitialized()
    {
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

    public void AddLocalRoll(RollResult result)
    {
        _history.Insert(0, result);
        StateHasChanged();
    }
}