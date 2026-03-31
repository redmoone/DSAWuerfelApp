using DsaWuerfelApp.Core.Dtos;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Core.Mappers;

public sealed class HeroMapper(
    HeroAttributesMapper heroAttributesMapper,
    HeroBadTraitsMapper heroBadTraitsMapper,
    HeroTalentsMapper heroTalentsMapper)
{
    public Hero Map(HeldenDatenDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Hero
        {
            Name = dto.Angaben.Name,
            Geschlecht = dto.Angaben.Geschlecht,
            Alter = dto.Angaben.Alter,
            Eigenschaften = heroAttributesMapper.Map(dto.Eigenschaften),
            SchlechteEigenschaften = heroBadTraitsMapper.Map(dto.SchlechteEigenschaften),
            Talente = heroTalentsMapper.Map(dto.Talentliste)
        };
    }
}