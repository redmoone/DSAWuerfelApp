using DsaWuerfelApp.Services.Auth;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DsaWuerfelApp.Tests.Infrastructure;

public sealed class TestApplicationFactory : WebApplicationFactory<Program>
{
    private readonly TestDatabase _database = new();

    public TestDatabase Database => _database;

    public FakeMagicLinkEmailSender EmailSender { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSolutionRelativeContentRoot("DsaWuerfelApp/DsaWuerfelApp");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HeroesDb"] = _database.ConnectionString,
                ["DataProtection:KeysPath"] = _database.DataProtectionPath,
                ["DataProtection:ApplicationName"] = "DsaWuerfelApp.Tests",
                ["MagicLinkAuth:ResendApiKey"] = "",
                ["MagicLinkAuth:FromEmail"] = "",
                ["PublicBaseUrl"] = "http://localhost"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.TestScheme;
                options.DefaultChallengeScheme = TestAuthenticationHandler.TestScheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.TestScheme,
                _ => { });

            EmailSender = new FakeMagicLinkEmailSender();
            services.RemoveAll<IMagicLinkEmailSender>();
            services.AddSingleton<IMagicLinkEmailSender>(EmailSender);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _database.Dispose();
        }
    }
}
