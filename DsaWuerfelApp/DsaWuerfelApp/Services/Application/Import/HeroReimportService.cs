using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services.Application.Import;

using Microsoft.EntityFrameworkCore;

namespace DsaWuerfelApp.Services;

public sealed class HeroReimportService(
    HeroDbContext dbContext,
    HeroImportService heroImportService,
    ILogger<HeroReimportService> logger)
{
    public async Task UpgradeStoredHeroesAsync(CancellationToken cancellationToken = default)
    {
        var outdatedHeroes = await dbContext.Heroes
            .Where(hero => hero.ImportVersion < HeroImportVersioning.CurrentVersion)
            .ToListAsync(cancellationToken);

        var heroesToUpgrade = outdatedHeroes
            .Where(hero => hero.SourceXml is { Length: > 0 })
            .ToList();

        if (heroesToUpgrade.Count == 0)
        {
            return;
        }

        var upgradedCount = 0;

        foreach (var hero in heroesToUpgrade)
        {
            try
            {
                heroImportService.Reimport(hero);
                upgradedCount++;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Held {HeroId} ({HeroName}) konnte nicht automatisch neu importiert werden.",
                    hero.Id,
                    hero.Name);
            }
        }

        if (upgradedCount == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "{Count} Helden wurden automatisch auf Importversion {Version} aktualisiert.",
            upgradedCount,
            HeroImportVersioning.CurrentVersion);
    }
}