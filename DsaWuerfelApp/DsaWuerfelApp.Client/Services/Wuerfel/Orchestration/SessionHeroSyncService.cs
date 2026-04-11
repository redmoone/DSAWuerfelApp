namespace DsaWuerfelApp.Client.Services;

public sealed class SessionHeroSyncService(
    GameClient gameClient,
    SessionState sessionState,
    ActiveHeroState activeHeroState)
{
    private bool _isAttached;
    private Guid? _lastHeroId;
    private string? _lastHeroName;
    private string? _lastSessionId;

    public async Task AttachAsync()
    {
        if (_isAttached)
        {
            return;
        }

        await activeHeroState.EnsureLoadedAsync();

        activeHeroState.Changed += HandleStateChanged;
        sessionState.ActiveSessionChanged += HandleStateChanged;
        _isAttached = true;

        await SyncAsync();
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        activeHeroState.Changed -= HandleStateChanged;
        sessionState.ActiveSessionChanged -= HandleStateChanged;
        _isAttached = false;
        _lastHeroId = null;
        _lastHeroName = null;
        _lastSessionId = null;
    }

    private void HandleStateChanged()
    {
        _ = SyncAsync();
    }

    private async Task SyncAsync()
    {
        var sessionId = sessionState.ActiveSessionId;
        if (string.IsNullOrWhiteSpace(sessionId) || !gameClient.IsConnected)
        {
            return;
        }

        var currentHeroId = activeHeroState.CurrentHero?.Id;
        var currentHeroName = activeHeroState.CurrentHero?.Name;
        if (string.Equals(_lastSessionId, sessionId, StringComparison.Ordinal) &&
            _lastHeroId == currentHeroId &&
            string.Equals(_lastHeroName, currentHeroName, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await gameClient.UpdateActiveHero(
                sessionId,
                currentHeroId,
                currentHeroName);
            _lastSessionId = sessionId;
            _lastHeroId = currentHeroId;
            _lastHeroName = currentHeroName;
        }
        catch
        {
        }
    }
}
