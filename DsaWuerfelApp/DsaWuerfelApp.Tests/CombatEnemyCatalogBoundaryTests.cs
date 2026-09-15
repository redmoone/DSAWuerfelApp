using System.Net;
using System.Net.Http.Json;

using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace DsaWuerfelApp.Tests;

public sealed class CombatEnemyCatalogBoundaryTests
{
    [Fact]
    public async Task Authenticated_catalog_endpoint_returns_all_dsa41_profiles()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "catalog-user");

        using var response = await client.GetAsync("/api/dice/combat-enemies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var enemies = await response.Content.ReadFromJsonAsync<CombatEnemyCatalogEntryDto[]>();
        Assert.NotNull(enemies);
        Assert.Equal(25, enemies!.Length);
        Assert.All(
            new[] { "goblin", "mittellaender", "ork", "zwerg" },
            id => Assert.Contains(enemies, enemy => enemy.Id == id));
        Assert.All(enemies, enemy => Assert.Contains(enemy.SourceRefs, source => !string.IsNullOrWhiteSpace(source.SourceId)));
    }

    [Fact]
    public void Catalog_is_strict_dsa41_and_contains_all_expected_profiles()
    {
        using var factory = new TestApplicationFactory();
        var store = factory.Services.GetRequiredService<CombatEnemyCatalogStore>();

        Assert.Equal("dsa41-gm-enemy-catalog", store.Catalog.Format.Id);
        Assert.Equal(1, store.Catalog.Format.SchemaVersion);
        Assert.Equal("DSA 4.1", store.Catalog.Ruleset.Name);
        Assert.Equal("4.1", store.Catalog.Ruleset.Edition);
        Assert.True(store.Catalog.Ruleset.Strict);
        Assert.Contains("DSA 5", store.Catalog.Ruleset.DoNotMixWith);
        Assert.Equal(3, store.Catalog.Sources.Length);
        Assert.Equal(17, store.Catalog.SpecialRuleDefinitions.Length);
        Assert.Equal(25, store.Entries.Count);
        Assert.Equal(25, store.Entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(store.Catalog.Sources, source => Assert.Equal("DSA 4.1", source.Edition));
    }

    [Fact]
    public void Catalog_preserves_readiness_variants_and_unresolved_resource_ranges()
    {
        using var factory = new TestApplicationFactory();
        var store = factory.Services.GetRequiredService<CombatEnemyCatalogStore>();

        Assert.All(
            new[] { "goblin", "mittellaender", "ork", "zwerg" },
            id =>
            {
                Assert.True(store.TryGetEntry(id, out var enemy));
                Assert.Equal("humanoid", enemy.Kind);
                Assert.False(enemy.CombatReady);
                Assert.Equal("requiresEquipmentSelection", enemy.Readiness);
                Assert.Equal(3, enemy.CombatVariants.Length);
                Assert.True(enemy.Equipment?.SelectionRequired);
            });

        Assert.True(store.TryGetEntry("rotfuchs", out var fox));
        Assert.False(fox.CombatReady);
        Assert.Equal("sourceDependent", fox.Readiness);
        Assert.Equal(13, fox.Combat!.Resources.LeP.SourceRange!.Min);
        Assert.Equal(16, fox.Combat.Resources.LeP.SourceRange.Max);
        Assert.Null(fox.Combat.Resources.LeP.Maximum);
        Assert.Null(fox.Combat.Resources.LeP.Initial);
    }
}
