namespace DsaWuerfelApp.Services.Auth;

public sealed class MagicLinkAuthOptions
{
    public const string SectionName = "MagicLinkAuth";
    public string PublicBaseUrl { get; set; } = string.Empty;

    public bool HasValidPublicBaseUrl(bool isDevelopment) =>
        Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (isDevelopment && uri.Scheme == Uri.UriSchemeHttp)) &&
        !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    public string ResendApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "DSA Würfelrunde";
    public int TokenLifetimeMinutes { get; set; } = 15;
    public int RequestCooldownSeconds { get; set; } = 60;
}
