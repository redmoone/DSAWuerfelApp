using DsaWuerfelApp.Core.Dtos;

namespace DsaWuerfelApp.Core.Mappers;

public sealed class HeroBadTraitsMapper
{
    public Dictionary<string, int> Map(IEnumerable<SchlechteEigenschaftDto> badTraits)
    {
        ArgumentNullException.ThrowIfNull(badTraits);

        return badTraits
            .Where(entry => entry.Wert > 0)
            .GroupBy(GetName, StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.Max(entry => entry.Wert), StringComparer.Ordinal);
    }

    private static string GetName(SchlechteEigenschaftDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.Bezeichner))
        {
            return dto.Bezeichner.Trim();
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return string.Empty;
        }

        var separatorIndex = dto.Name.LastIndexOf(':');
        return separatorIndex > 0
            ? dto.Name[..separatorIndex].Trim()
            : dto.Name.Trim();
    }
}