using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class HeroContextReader(
    IHeroReadRepository heroReadRepository)
{
    public Task<Hero?> LoadOptionalAsync(Guid? heroId, string userId, CancellationToken cancellationToken = default)
    {
        return heroId.HasValue
            ? heroReadRepository.GetOwnedByIdAsync(heroId.Value, userId, cancellationToken)
            : heroReadRepository.GetActiveAsync(userId, cancellationToken);
    }

    public async Task<Hero> LoadRequiredForMasterAsync(Guid heroId, string targetUserId, CancellationToken cancellationToken = default)
    {
        return await heroReadRepository.GetOwnedByIdAsync(heroId, targetUserId, cancellationToken)
               ?? throw new RequestRejectedException(RequestRejectionReason.NotFound, "Der ausgew?hlte Held konnte nicht geladen werden.");
    }

    public async Task<Hero> LoadRequiredAsync(Guid heroId, string userId, CancellationToken cancellationToken = default)
    {
        return await heroReadRepository.GetOwnedByIdAsync(heroId, userId, cancellationToken)
               ?? throw new InvalidOperationException("Der ausgewählte Held konnte nicht geladen werden.");
    }

}
