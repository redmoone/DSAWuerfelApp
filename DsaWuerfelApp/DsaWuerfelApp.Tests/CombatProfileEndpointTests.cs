using System.Net;
using System.Net.Http.Json;
using System.Text;

using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;
using DsaWuerfelApp.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace DsaWuerfelApp.Tests;

public sealed class CombatProfileEndpointTests
{
    [Fact]
    public async Task Returns_owned_profile_without_source_xml()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        var hero = await Seed(factory, "owner", CombatProfileMappingTests.CordulaXmlForEndpoint, "Cordula");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "owner");

        var response = await client.GetAsync($"/api/heroes/{hero.Id}/combat-profile");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var profile = await response.Content.ReadFromJsonAsync<CombatProfileDto>();
        Assert.NotNull(profile);
        Assert.Equal(hero.Id, profile!.HeroId);
        Assert.Equal(27, Assert.Single(profile.Sets).Weapons.Single().RangedValue);
        Assert.DoesNotContain("SourceXml", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<daten", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_foreign_hero_and_missing_source()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        var foreignHero = await Seed(factory, "owner", CombatProfileMappingTests.CordulaXmlForEndpoint, "Cordula");
        var missingSourceHero = await Seed(factory, "owner", null, "Altbestand");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "outsider");

        Assert.Equal(HttpStatusCode.NotFound,
            await GetStatus(client, $"/api/heroes/{foreignHero.Id}/combat-profile"));

        client.DefaultRequestHeaders.Remove("X-Test-User-Id");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "owner");
        var missingResponse = await client.GetAsync($"/api/heroes/{missingSourceHero.Id}/combat-profile");
        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        Assert.Contains("erneut importieren", await missingResponse.Content.ReadAsStringAsync());
    }

    private static async Task<HttpStatusCode> GetStatus(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        return response.StatusCode;
    }

    private static async Task<Hero> Seed(TestApplicationFactory factory, string owner, string? sourceXml, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
        var hero = new Hero
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Name = name,
            ImportVersion = 3,
            SourceXml = sourceXml is null ? null : Encoding.UTF8.GetBytes(sourceXml)
        };
        db.Heroes.Add(hero);
        await db.SaveChangesAsync();
        return hero;
    }
}
