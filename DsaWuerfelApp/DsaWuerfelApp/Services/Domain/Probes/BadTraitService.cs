using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class BadTraitService
{
    public BadTraitDto[] BuildBadTraits(Hero? hero)
    {
        if (hero is null || hero.SchlechteEigenschaften.Count == 0)
        {
            return [];
        }

        return hero.SchlechteEigenschaften
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => BuildBadTrait(entry.Key, entry.Value))
            .ToArray();
    }

    public BadTraitDto? ResolveBadTrait(Hero? hero, string? badTraitName)
    {
        if (hero is null || string.IsNullOrWhiteSpace(badTraitName))
        {
            return null;
        }

        return hero.SchlechteEigenschaften.TryGetValue(badTraitName, out var value)
            ? BuildBadTrait(badTraitName, value)
            : null;
    }

    private static BadTraitDto BuildBadTrait(string name, int value)
    {
        return new BadTraitDto(name, value, value, value <= 0 ? 0 : (value + 1) / 2);
    }
}
