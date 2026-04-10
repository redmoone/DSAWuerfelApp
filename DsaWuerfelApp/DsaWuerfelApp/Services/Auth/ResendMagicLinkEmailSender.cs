using System.Net.Http.Headers;

using Microsoft.Extensions.Options;

namespace DsaWuerfelApp.Services.Auth;

public interface IMagicLinkEmailSender
{
    Task SendAsync(string email, string magicLink, CancellationToken cancellationToken = default);
}

public sealed class ResendMagicLinkEmailSender(
    HttpClient httpClient,
    IOptions<MagicLinkAuthOptions> options,
    IWebHostEnvironment environment,
    ILogger<ResendMagicLinkEmailSender> logger) : IMagicLinkEmailSender
{
    private readonly MagicLinkAuthOptions _options = options.Value;

    public async Task SendAsync(string email, string magicLink, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ResendApiKey))
        {
            if (environment.IsDevelopment())
            {
                logger.LogInformation(
                    "Magic link for {Email}: {MagicLink}",
                    email,
                    magicLink);
                return;
            }

            throw new InvalidOperationException("Resend API key ist nicht konfiguriert.");
        }

        if (string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            throw new InvalidOperationException("MagicLinkAuth:FromEmail ist nicht konfiguriert.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);
        request.Content = JsonContent.Create(new
        {
            from = BuildFromAddress(),
            to = new[] { email },
            subject = "Dein Magic Link für DSA Würfelrunde",
            html = BuildHtmlBody(magicLink),
            text = $"Mit diesem Link anmelden: {magicLink}"
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogError(
            "Resend magic link request failed with status {StatusCode}: {Response}",
            response.StatusCode,
            errorBody);
        throw new InvalidOperationException("Magic Link konnte nicht versendet werden.");
    }

    private string BuildFromAddress()
    {
        return string.IsNullOrWhiteSpace(_options.FromName)
            ? _options.FromEmail
            : $"{_options.FromName} <{_options.FromEmail}>";
    }

    private static string BuildHtmlBody(string magicLink)
    {
        return $"""
                <div style="font-family: Arial, sans-serif; color: #0f1a29; line-height: 1.6;">
                  <h2 style="margin-bottom: 12px;">DSA Würfelrunde</h2>
                  <p>Mit diesem Link meldest du dich in deiner Runde an.</p>
                  <p style="margin: 24px 0;">
                    <a href="{magicLink}" style="background:#1c2e4a;color:#d4af37;padding:12px 20px;border-radius:999px;text-decoration:none;font-weight:700;">
                      Jetzt anmelden
                    </a>
                  </p>
                  <p>Falls der Button nicht funktioniert, nutze diesen Link:</p>
                  <p><a href="{magicLink}">{magicLink}</a></p>
                  <p>Der Link ist nur kurz gültig und kann genau einmal verwendet werden.</p>
                </div>
                """;
    }
}