using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Tests.Infrastructure;

namespace DsaWuerfelApp.Tests;

public sealed class HiddenRollTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public HiddenRollTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public Task Free_roll_with_hidden_flag_is_rejected() =>
        AssertHiddenRejectedAsync(
            "/api/dice/free-roll",
            new FreeRollRequestDto(null, [new DiceRollGroupDto(20, 1)], 0, true));

    [Fact]
    public Task Talent_roll_with_hidden_flag_is_rejected() =>
        AssertHiddenRejectedAsync(
            "/api/dice/talent-roll",
            new TalentRollRequestDto(null, null, "Athletik", 0, null, [], null, true));

    [Fact]
    public Task Attribute_roll_with_hidden_flag_is_rejected() =>
        AssertHiddenRejectedAsync(
            "/api/dice/attribute-roll",
            new AttributeRollRequestDto(null, null, ["MU"], 0, null, true));

    [Fact]
    public Task Bad_trait_roll_with_hidden_flag_is_rejected() =>
        AssertHiddenRejectedAsync(
            "/api/dice/bad-trait-roll",
            new BadTraitRollRequestDto(null, null, "Aberglaube", 5, null, true));

    private async Task AssertHiddenRejectedAsync(string path, object request)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        using var response = await client.PostAsJsonAsync(path, request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
        Assert.Equal("Verdeckte Würfe sind derzeit nicht verfügbar.", error!["error"]);
    }
}
