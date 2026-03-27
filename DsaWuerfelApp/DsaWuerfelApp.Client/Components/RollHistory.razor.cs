using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class RollHistory : IDisposable
{
    private readonly List<RollResult> _history = new();
    [Inject] public GameClient GameClient { get; set; } = null!;

    public void Dispose()
    {
        GameClient.OnRollResultReceived -= HandleRollResult;
        GameClient.OnTalentProbeResultReceived -= HandleTalentProbeResult;
    }

    protected override void OnInitialized()
    {
        GameClient.OnRollResultReceived += HandleRollResult;
        GameClient.OnTalentProbeResultReceived += HandleTalentProbeResult;
    }

    private void HandleRollResult(RollResult result)
    {
        _history.Insert(0, result);
        StateHasChanged();
    }

    public void AddEntry(RollResult result)
    {
        _history.Insert(0, result);
        StateHasChanged();
    }

    public void AddLocalRoll(RollResult result)
    {
        AddEntry(result);
    }

    private void HandleTalentProbeResult(TalentProbeResult result)
    {
        AddEntry(new RollResult
        {
            PlayerName = result.PlayerName,
            Timestamp = result.Timestamp,
            Rolls = result.Rolls,
            Modifier = 0,
            TotalSum = result.Rolls.Sum(roll => roll.Value)
        });
    }
}