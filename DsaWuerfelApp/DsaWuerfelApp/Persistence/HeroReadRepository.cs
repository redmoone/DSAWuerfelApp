using DsaWuerfelApp.Shared.Models;

using Microsoft.EntityFrameworkCore;

namespace DsaWuerfelApp.Persistence;

public sealed class HeroReadRepository(HeroDbContext dbContext) : IHeroReadRepository
{
    public Task<Hero?> GetOwnedByIdAsync(Guid heroId, string ownerUserId, CancellationToken cancellationToken = default)
    {
        return dbContext.Heroes
            .AsNoTracking()
            .FirstOrDefaultAsync(hero => hero.Id == heroId && hero.OwnerUserId == ownerUserId, cancellationToken);
    }

    public Task<Hero?> GetActiveAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        return dbContext.Heroes
            .AsNoTracking()
            .FirstOrDefaultAsync(hero => hero.OwnerUserId == ownerUserId && hero.IsActive, cancellationToken);
    }
}
