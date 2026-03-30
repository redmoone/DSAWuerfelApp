using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelSignalREventBridge(WuerfelState state, GameClient gameClient)
{
    private bool _isAttached;

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        gameClient.OnFreeRollResultReceived += HandleFreeRollResultReceived;
        gameClient.OnTalentRollResultReceived += HandleTalentRollResultReceived;
        gameClient.OnAttributeRollResultReceived += HandleAttributeRollResultReceived;
        gameClient.OnBadTraitRollResultReceived += HandleBadTraitRollResultReceived;
        _isAttached = true;
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        gameClient.OnFreeRollResultReceived -= HandleFreeRollResultReceived;
        gameClient.OnTalentRollResultReceived -= HandleTalentRollResultReceived;
        gameClient.OnAttributeRollResultReceived -= HandleAttributeRollResultReceived;
        gameClient.OnBadTraitRollResultReceived -= HandleBadTraitRollResultReceived;
        _isAttached = false;
    }

    private void HandleFreeRollResultReceived(FreeRollResultDto result)
    {
        state.ApplyFreeRollResult(result);
    }

    private void HandleTalentRollResultReceived(TalentRollResultDto result)
    {
        state.ApplyTalentRollResult(result);
    }

    private void HandleAttributeRollResultReceived(AttributeRollResultDto result)
    {
        state.ApplyAttributeRollResult(result);
    }

    private void HandleBadTraitRollResultReceived(BadTraitRollResultDto result)
    {
        state.ApplyBadTraitRollResult(result);
    }
}