using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class SpellOptionResolver(
    SpellSelectionResolver spellSelectionResolver,
    SpellInfoSectionFactory spellInfoSectionFactory,
    SpellSelectionPanelFactory spellSelectionPanelFactory)
{
    internal bool TryResolveHeroSelection(
        Hero hero,
        string spellName,
        TalentData spell,
        ParsedProbeSelection selection,
        IReadOnlyList<string> spellOptionValues,
        out ResolvedHeroSpellSelection resolvedSelection)
    {
        return spellSelectionResolver.TryResolve(
            hero,
            spellName,
            spell,
            selection,
            spellOptionValues,
            out resolvedSelection);
    }

    public IReadOnlyList<ProbeInfoSectionDto> BuildSpellInfoSections(Hero hero, string spellName, SpellCatalogEntry spellEntry)
    {
        return spellInfoSectionFactory.Build(hero, spellName, spellEntry);
    }

    public SpellSelectionPanelDto? BuildSpellSelectionPanel(Hero hero, ResolvedProbeData resolvedProbe)
    {
        return spellSelectionPanelFactory.Build(hero, resolvedProbe);
    }
}
