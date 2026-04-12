using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class HeroSpellIndexBuilder
{
    public Dictionary<string, TalentData> Build(Hero hero)
    {
        return hero.Zauber.ToDictionary(
            entry => entry.Key,
            entry => CloneProbeData(entry.Value),
            StringComparer.Ordinal);
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
