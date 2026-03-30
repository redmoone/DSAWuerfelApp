namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelContextSubscription(
    ActiveHeroState activeHeroState,
    WuerfelContextService contextService)
{
    private bool _isAttached;

    public async Task AttachAsync()
    {
        if (_isAttached)
        {
            return;
        }

        await activeHeroState.EnsureLoadedAsync();
        activeHeroState.Changed += HandleActiveHeroChanged;
        _isAttached = true;

        await contextService.LoadContextAsync();
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        activeHeroState.Changed -= HandleActiveHeroChanged;
        _isAttached = false;
    }

    private void HandleActiveHeroChanged()
    {
        _ = contextService.LoadContextAsync();
    }
}