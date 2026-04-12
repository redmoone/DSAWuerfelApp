using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class HeroProbeCatalogBuilder(
    HeroAttributeValueFactory heroAttributeValueFactory,
    ProbeSearchEntryFactory probeSearchEntryFactory,
    HeroTalentIndexBuilder heroTalentIndexBuilder,
    HeroSpellIndexBuilder heroSpellIndexBuilder)
{
    public AttributeValueDto[] BuildAttributeValues(Hero? hero)
    {
        return heroAttributeValueFactory.Build(hero);
    }

    public ProbeSearchEntryDto[] BuildCatalogProbes()
    {
        return probeSearchEntryFactory.BuildCatalogProbes();
    }

    public ProbeSearchEntryDto[] BuildKnownProbes(Hero hero)
    {
        return probeSearchEntryFactory.BuildKnownProbes(hero);
    }

    internal Dictionary<string, KnownTalentEntry> BuildKnownTalentMap(Hero hero)
    {
        return heroTalentIndexBuilder.Build(hero);
    }

    public Dictionary<string, TalentData> BuildKnownSpellMap(Hero hero)
    {
        return heroSpellIndexBuilder.Build(hero);
    }

    internal bool IsTalentRollable(string talentName, KnownTalentEntry talent)
    {
        return heroTalentIndexBuilder.IsTalentRollable(talentName, talent);
    }

    public bool IsRitualKnowledgeTalent(string talentName)
    {
        return heroTalentIndexBuilder.IsRitualKnowledgeTalent(talentName);
    }
}
