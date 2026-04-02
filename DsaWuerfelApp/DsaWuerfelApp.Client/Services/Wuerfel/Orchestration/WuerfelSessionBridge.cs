using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelSessionBridge(WuerfelState state, SessionState sessionState)
{
    private bool _isAttached;

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        sessionState.ActiveSessionChanged += HandleActiveSessionChanged;
        ApplyActiveSessionHistory();
        _isAttached = true;
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        sessionState.ActiveSessionChanged -= HandleActiveSessionChanged;
        _isAttached = false;
    }

    private void HandleActiveSessionChanged()
    {
        ApplyActiveSessionHistory();
    }

    private void ApplyActiveSessionHistory()
    {
        var history = sessionState.ActiveSession?.History ?? Array.Empty<RollHistoryEntryDto>();
        state.SetHistory(history);
    }
}