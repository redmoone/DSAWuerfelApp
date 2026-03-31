using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class HeroContextReader(IHeroReadRepository heroReadRepository)
{
    public Task<Hero?> LoadOptionalAsync(Guid? heroId, CancellationToken cancellationToken = default)
    {
        return heroId.HasValue
            ? heroReadRepository.GetByIdAsync(heroId.Value, cancellationToken)
            : heroReadRepository.GetActiveAsync(cancellationToken);
    }

    public async Task<Hero> LoadRequiredAsync(Guid heroId, CancellationToken cancellationToken = default)
    {
        return await heroReadRepository.GetByIdAsync(heroId, cancellationToken)
               ?? throw new InvalidOperationException("Der ausgewählte Held konnte nicht geladen werden.");
    }
}