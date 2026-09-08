using System.Net;
using System.Net.Http.Json;
using DsaWuerfelApp.Services.Auth;
using DsaWuerfelApp.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DsaWuerfelApp.Tests;

public class AuthRedirectTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/kampf", "/kampf")]
    [InlineData("/kampf?tab=1", "/kampf?tab=1")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    public async Task Login_uses_configured_origin_and_local_redirect(string redirect, string expected)
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Host = "evil.example";
        var response = await client.PostAsJsonAsync("/api/auth/magic-link/request", new { Email = "user@example.test", RedirectPath = redirect });
        response.EnsureSuccessStatusCode();
        var mail = Assert.Single(factory.EmailSender.SentMessages);
        Assert.StartsWith("https://login.example/app/auth/magic-link/verify?token=", mail.Link);
        var token = new Uri(mail.Link).Query;
        var verified = await client.GetAsync("/auth/magic-link/verify" + token);
        Assert.Equal(HttpStatusCode.Redirect, verified.StatusCode);
        Assert.Equal(expected, verified.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("", false, false)]
    [InlineData("http://example.test", false, false)]
    [InlineData("http://localhost:5206", true, true)]
    [InlineData("https://example.test/app", false, true)]
    [InlineData("https://user@example.test", false, false)]
    [InlineData("https://example.test/?x=1", false, false)]
    [InlineData("https://example.test/#x", false, false)]
    public void Origin_configuration_is_validated(string origin, bool development, bool valid) =>
        Assert.Equal(valid, new MagicLinkAuthOptions { PublicBaseUrl = origin }.HasValidPublicBaseUrl(development));

    [Fact]
    public void Missing_public_origin_rejects_host_startup()
    {
        using var factory = new TestApplicationFactory();
        using var invalid = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.PostConfigure<MagicLinkAuthOptions>(services, options => options.PublicBaseUrl = "")));
        var error = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => invalid.CreateClient());
        Assert.Contains("PublicBaseUrl", error.Message);
    }
}
