using System.Net;
using System.Net.Http.Json;

using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Tests.Infrastructure;

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
}
