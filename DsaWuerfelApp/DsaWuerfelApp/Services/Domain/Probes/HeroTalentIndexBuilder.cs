using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class HeroTalentIndexBuilder(
    TalentCatalogStore talentCatalogStore,
    DerivedTalentService derivedTalentService)
{
    private const string RitualKnowledgeTalentPrefix = "Ritualkenntnis:";

    internal Dictionary<string, KnownTalentEntry> Build(Hero hero)
    {
        var knownTalents = hero.Talente.ToDictionary(
            entry => entry.Key,
            entry => new KnownTalentEntry(CloneProbeData(entry.Value), true),
            StringComparer.Ordinal);

        foreach (var catalogEntry in talentCatalogStore.Entries)
        {
            if (TalentCatalogText.TryFindBestNameMatch(hero.Talente, catalogEntry.Name, out var existingTalentName,
                    out _))
            {
                var existingTalent = knownTalents[existingTalentName];
                if (string.IsNullOrWhiteSpace(existingTalent.Talent.Probe))
                {
                    existingTalent.Talent.Probe = catalogEntry.Probe;
                }

                continue;
            }

            knownTalents[catalogEntry.Name] = new KnownTalentEntry(
                new TalentData { Wert = 0, Probe = catalogEntry.Probe, Specializations = [] },
                false);
        }

        derivedTalentService.AddDerivedTalents(knownTalents);
        return knownTalents;
    }

    internal bool IsTalentRollable(string talentName, KnownTalentEntry talent)
    {
        if (IsRitualKnowledgeTalent(talentName))
        {
            return false;
        }

        return talent.IsOwnedByHero ||
               talentCatalogStore.TryGetEntry(talentName, out var catalogEntry) && catalogEntry.IsBasisTalent;
    }

    public bool IsRitualKnowledgeTalent(string talentName)
    {
        return talentName.StartsWith(RitualKnowledgeTalentPrefix, StringComparison.Ordinal);
    }

    private static TalentData CloneProbeData(TalentData value)
    {
        return new TalentData
        {
            Wert = value.Wert,
            Probe = value.Probe,
            Specializations = value.Specializations.ToArray()
        };
    }
}
