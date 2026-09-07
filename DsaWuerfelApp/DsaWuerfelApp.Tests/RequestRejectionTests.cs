using System.Net;
using System.Net.Http.Json;

using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Tests.Infrastructure;

namespace DsaWuerfelApp.Tests;

public sealed class RequestRejectionTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public RequestRejectionTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Empty_master_request_is_validation_error()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");
        var request = new MasterAttributeRollRequestDto("session", [], ["MU"], 0, null);

        using var response = await client.PostAsJsonAsync("/api/dice/master-attribute-roll", request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("mindestens einen Spieler", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unexpected_probe_failure_is_internal_error_without_exception_text()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        var request = new TalentRollRequestDto(
            null,
            null,
            "not-a-real-talent",
            0,
            null,
            [],
            null,
            false);
        using var response = await client.PostAsJsonAsync("/api/dice/talent-roll", request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("interner Serverfehler", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-a-real-probe", body, StringComparison.OrdinalIgnoreCase);
    }
}
