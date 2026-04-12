using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class BadTraitResolver(BadTraitService badTraitService)
{
    public BadTraitDto? ResolveOptional(Hero? hero, string? badTraitName)
    {
        if (hero is null || string.IsNullOrWhiteSpace(badTraitName))
        {
            return null;
        }

        return badTraitService.ResolveBadTrait(hero, badTraitName);
    }

    public BadTraitDto ResolveRequired(Hero hero, string? badTraitName)
    {
        if (string.IsNullOrWhiteSpace(badTraitName))
        {
            throw new InvalidOperationException("Bitte zuerst eine relevante schlechte Eigenschaft waehlen.");
        }

        return badTraitService.ResolveBadTrait(hero, badTraitName)
               ?? throw new InvalidOperationException(
                   "Die ausgewaehlte schlechte Eigenschaft ist fuer den Helden nicht vorhanden.");
    }
}
