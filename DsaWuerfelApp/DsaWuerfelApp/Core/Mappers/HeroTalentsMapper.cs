using DsaWuerfelApp.Core.Dtos;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Core.Mappers;

public sealed class HeroTalentsMapper
{
    public Dictionary<string, TalentData> Map(IEnumerable<TalentDto> talents)
    {
        ArgumentNullException.ThrowIfNull(talents);

        return talents.ToDictionary(
            talent => talent.Name,
            talent => new TalentData
            {
                Wert = talent.Wert,
                Probe = TalentCatalogText.NormalizeAttributeProbe(talent.Probe),
                Specializations = TalentCatalogText.ParseSpecializations(talent.Specializations)
            });
    }
}