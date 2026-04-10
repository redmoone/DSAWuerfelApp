namespace DsaWuerfelApp.Services.Auth;

public sealed class MagicLinkAuthOptions
{
    public const string SectionName = "MagicLinkAuth";

    public string ResendApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "DSA Würfelrunde";
    public int TokenLifetimeMinutes { get; set; } = 15;
    public int RequestCooldownSeconds { get; set; } = 60;
}