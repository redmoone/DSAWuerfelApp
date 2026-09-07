using System.Net;

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
        Assert.True(File.Exists(_factory.Database.DatabasePath));
        Assert.StartsWith(Path.GetTempPath(), _factory.Database.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_factory.EmailSender.SentMessages);
    }
}
