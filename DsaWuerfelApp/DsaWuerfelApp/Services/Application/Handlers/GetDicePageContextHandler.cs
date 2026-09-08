using System.Diagnostics;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class GetDicePageContextHandler(
    HeroContextReader heroContextReader,
    DicePageContextFactory dicePageContextFactory)
{
    public async Task<DicePageContextDto> HandleAsync(Guid? heroId, string? sessionId, string userId, CancellationToken cancellationToken = default)
    {
        var hero = await heroContextReader.LoadContextAsync(heroId, sessionId, userId, cancellationToken);
        return dicePageContextFactory.BuildContext(hero, Debugger.IsAttached);
    }

    public Task<DicePageContextDto> HandleCatalogAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(dicePageContextFactory.BuildCatalogContext(Debugger.IsAttached));
    }
}
