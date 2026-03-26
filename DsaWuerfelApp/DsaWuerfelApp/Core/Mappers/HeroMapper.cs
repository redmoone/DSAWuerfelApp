using DsaWuerfelApp.Core.Dtos;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Core.Mappers;

public static class HeroMapper
{
    public static Hero Map(HeldenDatenDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Hero
        {
            Name = dto.Angaben.Name,
            Geschlecht = dto.Angaben.Geschlecht,
            Alter = dto.Angaben.Alter,
            Eigenschaften = new Dictionary<string, int>
            {
                { "MU", dto.Eigenschaften.Mut.Akt },
                { "KL", dto.Eigenschaften.Klugheit.Akt },
                { "IN", dto.Eigenschaften.Intuition.Akt },
                { "CH", dto.Eigenschaften.Charisma.Akt },
                { "FF", dto.Eigenschaften.Fingerfertigkeit.Akt },
                { "GE", dto.Eigenschaften.Gewandtheit.Akt },
                { "KO", dto.Eigenschaften.Konstitution.Akt },
                { "KK", dto.Eigenschaften.Koerperkraft.Akt }
            },
            Talente = dto.Talentliste.ToDictionary(t => t.Name, t => t.Wert)
        };
    }
}