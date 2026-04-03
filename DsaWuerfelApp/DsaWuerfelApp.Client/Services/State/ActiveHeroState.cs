using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Client.Services;

public sealed class ActiveHeroState : IDisposable
{
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

        CurrentHero = await _heroApiClient.GetActiveHeroAsync();

        if (CurrentHero is null)
        {
            CurrentHero = (await _heroApiClient.GetHeroesAsync()).FirstOrDefault();
        }

        Changed?.Invoke();
    }

    public void SetCurrentHero(Hero? hero)
    {
        CurrentHero = hero;
        Changed?.Invoke();
    }

    public void ClearIfMatches(Guid heroId)
    {
        if (CurrentHero?.Id != heroId)
        {
            return;
        }

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
        if (CurrentHero is null)
        {
            return;
        }

        CurrentHero = null;
        Changed?.Invoke();
    }
}