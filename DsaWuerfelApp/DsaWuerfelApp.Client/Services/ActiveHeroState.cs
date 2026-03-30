using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Client.Services;

public sealed class ActiveHeroState
{
    private readonly IHeroApiClient _heroApiClient;

    public ActiveHeroState(IHeroApiClient heroApiClient)
    {
        _heroApiClient = heroApiClient;
    }

    public Hero? CurrentHero { get; private set; }

    public event Action? Changed;

    public async Task EnsureLoadedAsync()
    {
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
}