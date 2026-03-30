using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Persistence;

public interface IHeroReadRepository
{
    Task<Hero?> GetByIdAsync(Guid heroId, CancellationToken cancellationToken = default);
    Task<Hero?> GetActiveAsync(CancellationToken cancellationToken = default);
}