using DsaWuerfelApp.Shared.Models;

using Microsoft.EntityFrameworkCore;

namespace DsaWuerfelApp.Persistence;

public sealed class HeroReadRepository(HeroDbContext dbContext) : IHeroReadRepository
{
    public Task<Hero?> GetByIdAsync(Guid heroId, CancellationToken cancellationToken = default)
    {
        return dbContext.Heroes
            .AsNoTracking()
            .FirstOrDefaultAsync(hero => hero.Id == heroId, cancellationToken);
    }

    public Task<Hero?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Heroes
            .AsNoTracking()
            .FirstOrDefaultAsync(hero => hero.IsActive, cancellationToken);
    }
}