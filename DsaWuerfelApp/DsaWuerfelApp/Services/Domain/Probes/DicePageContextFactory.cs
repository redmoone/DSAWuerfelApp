using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class DicePageContextFactory(
    HeroProbeCatalogBuilder heroProbeCatalogBuilder,
    BadTraitService badTraitService)
{
    public DicePageContextDto BuildContext(Hero? hero, bool showDebugForcedRolls)
    {
        var hasKnownProbes = hero is not null && (hero.Talente.Count > 0 || hero.Zauber.Count > 0);

        return new DicePageContextDto(
            hero?.Id,
            hero?.Name,
            heroProbeCatalogBuilder.BuildAttributeValues(hero),
            hasKnownProbes ? heroProbeCatalogBuilder.BuildKnownProbes(hero!) : DefaultProbeCatalog.CreateEntries(),
            badTraitService.BuildBadTraits(hero),
            hero is null ? "Nach Proben suchen..." : $"Talente und Zauber von {hero.Name} durchsuchen...",
            showDebugForcedRolls);
    }

    public DicePageContextDto BuildCatalogContext(bool showDebugForcedRolls)
    {
        return new DicePageContextDto(
            null,
            null,
            heroProbeCatalogBuilder.BuildAttributeValues(null),
            heroProbeCatalogBuilder.BuildCatalogProbes(),
            [],
            "Alle Talente und Zauber durchsuchen...",
            showDebugForcedRolls);
    }
}
