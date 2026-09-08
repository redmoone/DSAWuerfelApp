namespace DsaWuerfelApp.Client.Services;

public sealed class SessionHeroSyncService(
    GameClient gameClient,
    SessionState sessionState,
    ActiveHeroState activeHeroState,
    AuthState authState,
    ILogger<SessionHeroSyncService> logger)
{
    private bool _isAttached;
    private bool _running;
    private bool _pending;

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
        _pending = false;
    }

    private void HandleStateChanged()
    {
        _ = SyncAsync();
    }

    private async Task SyncAsync()
    {
        _pending = true;
        if (_running) return;
        _running = true;
        var sent = new HashSet<(string Session, Guid? Hero, string? Name)>();
        try
        {
            while (_pending && _isAttached)
            {
                _pending = false;
                var sessionId = sessionState.ActiveSessionId;
                if (string.IsNullOrWhiteSpace(sessionId) || !gameClient.IsConnected) return;
                var hero = activeHeroState.CurrentHero;
                var desired = (sessionId, hero?.Id, hero?.Name);
                var player = sessionState.ActiveSession?.SessionId == sessionId
                    ? sessionState.ActiveSession.Players.FirstOrDefault(player => player.UserId == authState.Current.User?.Id)
                    : null;
                if (player is null) return;
                if (player.ActiveHeroId == hero?.Id && player.ActiveHeroName == hero?.Name) continue;
                if (!sent.Add(desired)) continue;
                await gameClient.UpdateActiveHero(sessionId, hero?.Id, hero?.Name);
                var latest = activeHeroState.CurrentHero;
                if (sessionId != sessionState.ActiveSessionId || latest?.Id != hero?.Id || latest?.Name != hero?.Name)
                    _pending = true;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Aktiver Held konnte nicht mit der Sitzung synchronisiert werden.");
        }
        finally { _running = false; }
    }
}
