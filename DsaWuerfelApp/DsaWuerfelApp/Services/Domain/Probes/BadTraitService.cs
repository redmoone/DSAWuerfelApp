using System.Text.Json;
using System.Text.Json.Serialization;

using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class BadTraitService(IHostEnvironment environment)
{
    private const string CatalogFileName = "SchlechteEigenschaften.json";
    private const int DefaultCatalogValue = 10;

    private readonly Lazy<IReadOnlyList<string>> _catalogNames =
        new(() => LoadCatalogNames(ResolveCatalogPath(environment)));

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

    public BadTraitDto[] BuildCatalogBadTraits()
    {
        return _catalogNames.Value
            .Select(name => BuildBadTrait(name, DefaultCatalogValue))
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

    private static string ResolveCatalogPath(IHostEnvironment environment)
    {
        var candidatePaths = new[]
        {
            Path.Combine(environment.ContentRootPath, "Data", CatalogFileName),
            Path.Combine(AppContext.BaseDirectory, "Data", CatalogFileName)
        };

        return candidatePaths.FirstOrDefault(File.Exists) ?? candidatePaths[0];
    }

    private static IReadOnlyList<string> LoadCatalogNames(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(path);
            var items = JsonSerializer.Deserialize<List<BadTraitCatalogItem>>(stream) ?? [];

            return items
                .Select(item => TalentCatalogText.NormalizeCatalogText(item.Name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private sealed class BadTraitCatalogItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
