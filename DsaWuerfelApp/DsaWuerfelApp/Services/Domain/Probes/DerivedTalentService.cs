using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class DerivedTalentService(TalentCatalogStore talentCatalogStore)
{
    private const string SelfControlTalentName = "Selbstbeherrschung";
    private const string SenseSharpnessTalentName = "Sinnensch\u00E4rfe";
    private const string WatchKeepingTalentName = "Wache halten";
    private const string WatchKeepingFallbackProbe = "KL/IN/IN";

    internal void AddDerivedTalents(Dictionary<string, KnownTalentEntry> knownTalents)
    {
        if (TryCreateWatchKeepingTalent(knownTalents, out var watchKeepingTalent))
        {
            knownTalents[WatchKeepingTalentName] = new KnownTalentEntry(watchKeepingTalent, true);
        }
    }

    private bool TryCreateWatchKeepingTalent(
        IReadOnlyDictionary<string, KnownTalentEntry> knownTalents,
        out TalentData talent)
    {
        if (!TryGetOwnedTalent(knownTalents, SelfControlTalentName, out var selfControlTalent) ||
            !TryGetOwnedTalent(knownTalents, SenseSharpnessTalentName, out var senseSharpnessTalent))
        {
            talent = null!;
            return false;
        }

        var calculatedValue =
            (selfControlTalent.Talent.Wert + (2 * senseSharpnessTalent.Talent.Wert) + 1) / 3;
        var cappedValue = Math.Min(
            calculatedValue,
            Math.Min(
                selfControlTalent.Talent.Wert * 2,
                senseSharpnessTalent.Talent.Wert * 2));

        talent = new TalentData
        {
            Wert = cappedValue,
            Probe = ResolveWatchKeepingProbe(selfControlTalent.Talent, senseSharpnessTalent.Talent),
            Specializations = []
        };

        return true;
    }

    private bool TryGetOwnedTalent(
        IReadOnlyDictionary<string, KnownTalentEntry> knownTalents,
        string talentName,
        out KnownTalentEntry talent)
    {
        if (TalentCatalogText.TryFindBestNameMatch(
                knownTalents.Where(entry => entry.Value.IsOwnedByHero),
                static existingEntry => existingEntry.Key,
                talentName,
                out var matchedEntry))
        {
            talent = matchedEntry.Value;
            return true;
        }

        talent = null!;
        return false;
    }

    private string ResolveWatchKeepingProbe(TalentData selfControlTalent, TalentData senseSharpnessTalent)
    {
        if (!string.IsNullOrWhiteSpace(senseSharpnessTalent.Probe))
        {
            return senseSharpnessTalent.Probe;
        }

        if (!string.IsNullOrWhiteSpace(selfControlTalent.Probe))
        {
            return selfControlTalent.Probe;
        }

        if (talentCatalogStore.TryGetEntry(SenseSharpnessTalentName, out var senseSharpnessCatalogEntry) &&
            !string.IsNullOrWhiteSpace(senseSharpnessCatalogEntry.Probe))
        {
            return senseSharpnessCatalogEntry.Probe;
        }

        if (talentCatalogStore.TryGetEntry(SelfControlTalentName, out var selfControlCatalogEntry) &&
            !string.IsNullOrWhiteSpace(selfControlCatalogEntry.Probe))
        {
            return selfControlCatalogEntry.Probe;
        }

        return WatchKeepingFallbackProbe;
    }
}
