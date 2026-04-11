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

        return talentCatalogService.ResolveBadTrait(hero, badTraitName);
    }

    public BadTraitDto ResolveRequired(Hero hero, string? badTraitName)
    {
        if (string.IsNullOrWhiteSpace(badTraitName))
        {
            throw new InvalidOperationException("Bitte zuerst eine relevante schlechte Eigenschaft wählen.");
        }

        return talentCatalogService.ResolveBadTrait(hero, badTraitName)
               ?? throw new InvalidOperationException(
                   "Die ausgewählte schlechte Eigenschaft ist für den Helden nicht vorhanden.");
    }
}
