using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Client.Services;

public sealed class ActiveHeroState : IDisposable
{
    private long _loadVersion;
    private bool _disposed;
    private readonly AuthState _authState;
    private readonly IHeroApiClient _heroApiClient;

    public ActiveHeroState(IHeroApiClient heroApiClient, AuthState authState)
    {
        _heroApiClient = heroApiClient;
        _authState = authState;
        _authState.Changed += HandleAuthChanged;
    }

    public Hero? CurrentHero { get; private set; }

    public void Dispose()
    {
        _authState.Changed -= HandleAuthChanged;
        _disposed = true;
        ++_loadVersion;
    }

    public event Action? Changed;

    public async Task EnsureLoadedAsync()
    {
        if (!_authState.IsLoaded)
        {
            await _authState.EnsureLoadedAsync();
        }

        if (!_authState.Current.IsAuthenticated)
        {
            Clear();
            return;
        }

        if (CurrentHero is not null)
        {
            return;
        }

        var version = ++_loadVersion;
        var userId = _authState.Current.User?.Id;
        bool IsCurrent() => !_disposed && version == _loadVersion && _authState.Current.IsAuthenticated && userId == _authState.Current.User?.Id;
        Hero? hero;
        try
        {
            hero = await _heroApiClient.GetActiveHeroAsync();
            if (!IsCurrent()) return;
            if (hero is null) hero = (await _heroApiClient.GetHeroesAsync()).FirstOrDefault();
            if (!IsCurrent()) return;
        }
        catch (Exception) when (!IsCurrent()) { return; }
        CurrentHero = hero;

        Changed?.Invoke();
    }

    public void SetCurrentHero(Hero? hero)
    {
        ++_loadVersion;
        CurrentHero = hero;
        Changed?.Invoke();
    }

    public void ClearIfMatches(Guid heroId)
    {
        if (CurrentHero?.Id != heroId)
        {
            return;
        }

        ++_loadVersion;
        CurrentHero = null;
        Changed?.Invoke();
    }

    private void HandleAuthChanged()
    {
        if (!_authState.Current.IsAuthenticated)
        {
            Clear();
        }
    }

    private void Clear()
    {
        ++_loadVersion;
        if (CurrentHero is null)
        {
            return;
        }

        CurrentHero = null;
        Changed?.Invoke();
    }
}