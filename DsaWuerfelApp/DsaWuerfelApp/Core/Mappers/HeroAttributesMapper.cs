using DsaWuerfelApp.Core.Dtos;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Core.Mappers;

public sealed class HeroAttributesMapper
{
    public Dictionary<string, int> Map(EigenschaftenDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Dictionary<string, int>
        {
            [HeroAttributeCatalog.Mut] = dto.Mut.Akt,
            [HeroAttributeCatalog.Klugheit] = dto.Klugheit.Akt,
            [HeroAttributeCatalog.Intuition] = dto.Intuition.Akt,
            [HeroAttributeCatalog.Charisma] = dto.Charisma.Akt,
            [HeroAttributeCatalog.Fingerfertigkeit] = dto.Fingerfertigkeit.Akt,
            [HeroAttributeCatalog.Gewandtheit] = dto.Gewandtheit.Akt,
            [HeroAttributeCatalog.Konstitution] = dto.Konstitution.Akt,
            [HeroAttributeCatalog.Koerperkraft] = dto.Koerperkraft.Akt
        };
    }
}