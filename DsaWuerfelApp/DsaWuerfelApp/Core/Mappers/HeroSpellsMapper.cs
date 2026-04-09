using DsaWuerfelApp.Core.Dtos;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Core.Mappers;

public sealed class HeroSpellsMapper
{
    public Dictionary<string, TalentData> Map(IEnumerable<ZauberDto> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);

        return spells
            .Where(spell => !string.IsNullOrWhiteSpace(spell.Name))
            .ToDictionary(
                spell => spell.Name,
                spell => new TalentData
                {
                    Wert = spell.Wert,
                    Probe = TalentCatalogText.NormalizeAttributeProbe(spell.Probe),
                    Specializations = TalentCatalogText.ParseSpecializations(spell.Specializations)
                },
                StringComparer.Ordinal);
    }
}