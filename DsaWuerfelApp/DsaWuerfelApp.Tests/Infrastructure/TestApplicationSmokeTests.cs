using System.Net;
using System.Net.Http.Json;

using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;
using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DsaWuerfelApp.Tests;

public sealed class TestApplicationSmokeTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public TestApplicationSmokeTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_dice_request_is_rejected()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/dice/catalog-context");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_catalog_request_works_without_development_database()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        using var response = await client.GetAsync("/api/dice/catalog-context");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var context = await response.Content.ReadFromJsonAsync<DicePageContextDto>();
        Assert.NotNull(context);
        Assert.Contains(context!.AvailableProbes, entry => entry.DisplayLabel.StartsWith("Abvenenum", StringComparison.Ordinal));
        Assert.Contains(context.AvailableProbes, entry => entry.DisplayLabel.StartsWith("Akrobatik", StringComparison.Ordinal));
        Assert.True(File.Exists(_factory.Database.DatabasePath));
        Assert.StartsWith(Path.GetTempPath(), _factory.Database.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_factory.EmailSender.SentMessages);
    }

    [Fact]
    public async Task Catalog_spell_info_uses_new_rule_sections_without_hero()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        var probeValue = Uri.EscapeDataString(
            ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Spell, "Attributo"));
        using var response = await client.GetAsync($"/api/dice/probe-info?probeValue={probeValue}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<ProbeInfoResultDto>();
        Assert.NotNull(info);
        Assert.Contains(info!.Sections, section => section.Label == "Probe");
        Assert.Contains(info.Sections, section => section.Label == "Sonderregeln");
        Assert.Contains("Katalogeintrag", info.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_talent_info_contains_prepared_description_and_rules()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        var probeValue = Uri.EscapeDataString(
            ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Talent, "Akrobatik"));
        using var response = await client.GetAsync($"/api/dice/probe-info?probeValue={probeValue}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<ProbeInfoResultDto>();
        Assert.NotNull(info);
        Assert.Contains(info!.Sections, section => section.Label == "Beschreibung");
        Assert.Contains(info.Sections, section => section.Label == "Effektive Behinderung");
        Assert.Contains(info.Sections, section => section.Label == "Voraussetzung");
        Assert.Contains(info.Sections, section => section.Label == "Regelhinweise");
        Assert.Contains(info.Sections, section => section.Label == "Spezialisierungen");
    }

    [Fact]
    public async Task Catalog_talent_with_multiple_probes_requires_and_exposes_an_explicit_probe()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        using var contextResponse = await client.GetAsync("/api/dice/catalog-context");
        contextResponse.EnsureSuccessStatusCode();
        var context = await contextResponse.Content.ReadFromJsonAsync<DicePageContextDto>();
        var singing = Assert.Single(
            context!.AvailableProbes,
            entry => entry.DisplayLabel.StartsWith("Singen", StringComparison.Ordinal));

        Assert.False(singing.IsSelectable);
        Assert.Equal(2, singing.Alternatives.Length);

        var selectedAlternative = singing.Alternatives[1].Value;
        var probeValue = Uri.EscapeDataString(selectedAlternative);
        using var infoResponse = await client.GetAsync($"/api/dice/probe-info?probeValue={probeValue}");

        infoResponse.EnsureSuccessStatusCode();
        var info = await infoResponse.Content.ReadFromJsonAsync<ProbeInfoResultDto>();
        Assert.NotNull(info);
        var probeSection = Assert.Single(info!.Sections, section => section.Label == "Probe");
        Assert.Contains("IN/CH/CH", probeSection.Text, StringComparison.Ordinal);
        Assert.Contains("IN/CH/KO", probeSection.Text, StringComparison.Ordinal);
        Assert.Contains("alternative Talentprobe", info.DetailsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Talent_specialization_uses_hero_data_threshold_and_keeps_raw_taw()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        var hero = new Hero
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "test-user",
            Name = "Talenttest",
            Eigenschaften = new() { ["MU"] = 10, ["GE"] = 10, ["KK"] = 10 },
            Talente = new()
            {
                ["Akrobatik"] = new TalentData
                {
                    Wert = 7,
                    Probe = "MU/GE/KK",
                    Specializations = ["Balancieren", "Bodenakrobatik"]
                }
            }
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
            db.Heroes.Add(hero);
            await db.SaveChangesAsync();
        }

        using var contextResponse = await client.GetAsync($"/api/dice/context?heroId={hero.Id}");
        contextResponse.EnsureSuccessStatusCode();
        var context = await contextResponse.Content.ReadFromJsonAsync<DicePageContextDto>();
        var akrobatik = Assert.Single(
            context!.AvailableProbes,
            entry => entry.DisplayLabel.StartsWith("Akrobatik", StringComparison.Ordinal));
        Assert.Contains(akrobatik.Alternatives, alternative =>
            alternative.Label.Contains("Balancieren", StringComparison.Ordinal));
        Assert.DoesNotContain(akrobatik.Alternatives, alternative =>
            alternative.Label.Contains("Bodenakrobatik", StringComparison.Ordinal));

        var selectedSpecialization = ProbeSelectionValue.EncodeOption(
            ProbeSelectionKind.Talent,
            "Akrobatik",
            ProbeSelectionOptionKind.Specialization,
            "Balancieren",
            99);
        var request = new TalentRollRequestDto(
            null,
            hero.Id,
            selectedSpecialization,
            0,
            null,
            [],
            "10,10,10",
            false);

        using var response = await client.PostAsJsonAsync("/api/dice/talent-roll", request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TalentRollResultDto>();
        Assert.NotNull(result);
        Assert.Equal(7, result!.TalentValue);
        Assert.Equal(-2, result.SpecializationModifier);
        Assert.Equal(9, result.EffectiveTalentValue);
        Assert.Equal("Balancieren", result.SpecializationName);
    }

    [Fact]
    public async Task Spell_option_modifier_is_resolved_from_catalog_and_keeps_original_zfw()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        var hero = new Hero
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "test-user",
            Name = "Zaubertest",
            Eigenschaften = new() { ["KL"] = 10, ["FF"] = 10 },
            Zauber = new()
            {
                ["Abvenenum"] = new TalentData { Wert = 10, Probe = "KL/KL/FF" }
            }
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
            db.Heroes.Add(hero);
            await db.SaveChangesAsync();
        }

        var option = ProbeSelectionValue.EncodeOption(
            ProbeSelectionKind.Spell,
            "Abvenenum",
            ProbeSelectionOptionKind.SpellModification,
            "Zauberdauer: Verlängern");
        var request = new TalentRollRequestDto(
            null,
            hero.Id,
            ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Spell, "Abvenenum"),
            0,
            null,
            [option],
            "10,10,10",
            false);

        using var response = await client.PostAsJsonAsync("/api/dice/talent-roll", request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TalentRollResultDto>();
        Assert.NotNull(result);
        Assert.Equal(10, result!.SpellDetails!.OriginalZfw);
        Assert.Equal(-3, result.SpellDetails.AutomaticModifier);
        Assert.Equal(13, result.EffectiveTalentValue);
        Assert.Equal(13, result.SpellDetails.RawZfp);
        Assert.Equal(13, result.SpellDetails.AvailableZfp);
    }
}
