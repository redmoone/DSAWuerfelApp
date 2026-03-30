using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class BadTraitResolver(TalentCatalogService talentCatalogService)
{
    public BadTraitDto? ResolveOptional(Hero? hero, string? badTraitName)
    {
        if (hero is null || string.IsNullOrWhiteSpace(badTraitName))
        {
            return null;
        }

        return talentCatalogService.ResolveBadTrait(hero, badTraitName)
               ?? throw new InvalidOperationException(
                   "Die ausgewaehlte schlechte Eigenschaft ist fuer den Held nicht vorhanden.");
    }

    public BadTraitDto ResolveRequired(Hero hero, string? badTraitName)
    {
        if (string.IsNullOrWhiteSpace(badTraitName))
        {
            throw new InvalidOperationException("Bitte zuerst eine relevante schlechte Eigenschaft waehlen.");
        }

        return talentCatalogService.ResolveBadTrait(hero, badTraitName)
               ?? throw new InvalidOperationException(
                   "Die ausgewaehlte schlechte Eigenschaft ist fuer den Held nicht vorhanden.");
    }
}