using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class ProbeInfoService(
    BadTraitService badTraitService,
    ProbeResolutionService probeResolutionService,
    SpellOptionResolver spellOptionResolver,
    TalentCatalogStore talentCatalogStore,
    SpellCatalogStore spellCatalogStore)
{
    public ProbeInfoResultDto BuildProbeInfo(
        Hero? hero,
        string probeValue,
        int modifier,
        string? badTraitName,
        IReadOnlyList<string>? spellOptionValues = null)
    {
        if (string.IsNullOrWhiteSpace(probeValue))
        {
            return TalentProbeInfoBuilder.BuildEmptySelectionInfo();
        }

        var badTrait = badTraitService.ResolveBadTrait(hero, badTraitName);
        if (hero is not null &&
            probeResolutionService.TryResolveProbe(hero, probeValue, spellOptionValues ?? [], out var resolvedProbe))
        {
            var spellSelection = spellOptionResolver.BuildSpellSelectionPanel(hero, resolvedProbe);
            return TalentProbeInfoBuilder.BuildResolvedProbeInfo(
                hero,
                resolvedProbe,
                badTrait,
                ResolveInfoSections(hero, resolvedProbe),
                spellSelection,
                modifier);
        }

        return TalentProbeInfoBuilder.BuildFallbackInfo(probeValue, badTrait);
    }

    private IReadOnlyList<ProbeInfoSectionDto> ResolveInfoSections(Hero hero, ResolvedProbeData resolvedProbe)
    {
        return resolvedProbe.Kind switch
        {
            ProbeSelectionKind.Talent when talentCatalogStore.TryGetEntry(resolvedProbe.BaseName, out var talentEntry)
                => talentEntry.InfoSections,
            ProbeSelectionKind.Spell when spellCatalogStore.TryGetEntry(resolvedProbe.BaseName, out var spellEntry)
                => spellOptionResolver.BuildSpellInfoSections(hero, resolvedProbe.BaseName, spellEntry),
            _ => []
        };
    }
}
