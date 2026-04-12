using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class HeroAttributeValueFactory
{
    public AttributeValueDto[] Build(Hero? hero)
    {
        var source = hero?.Eigenschaften?.Count > 0 ? hero.Eigenschaften : HeroAttributeCatalog.DefaultValues;

        return HeroAttributeCatalog.Order
            .Select(attribute => new AttributeValueDto(attribute, source.GetValueOrDefault(attribute)))
            .Concat(source
                .Where(entry => !HeroAttributeCatalog.Order.Contains(entry.Key, StringComparer.Ordinal))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new AttributeValueDto(entry.Key, entry.Value)))
            .ToArray();
    }
}
