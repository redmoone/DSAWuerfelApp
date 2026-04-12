using System.Diagnostics;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class GetDicePageContextHandler(
    HeroContextReader heroContextReader,
    DicePageContextFactory dicePageContextFactory)
{
    public async Task<DicePageContextDto> HandleAsync(Guid? heroId, CancellationToken cancellationToken = default)
    {
        var hero = await heroContextReader.LoadOptionalAsync(heroId, cancellationToken);
        return dicePageContextFactory.BuildContext(hero, Debugger.IsAttached);
    }

    public Task<DicePageContextDto> HandleCatalogAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(dicePageContextFactory.BuildCatalogContext(Debugger.IsAttached));
    }
}
