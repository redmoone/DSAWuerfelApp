using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Persistence;

public interface IHeroReadRepository
{
    Task<Hero?> GetOwnedByIdAsync(Guid heroId, string ownerUserId, CancellationToken cancellationToken = default);
    Task<Hero?> GetActiveAsync(string ownerUserId, CancellationToken cancellationToken = default);
}
