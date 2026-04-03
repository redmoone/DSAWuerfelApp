using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

using DsaWuerfelApp.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DsaWuerfelApp.Services.Auth;

public sealed class MagicLinkService(
    HeroDbContext dbContext,
    IMagicLinkEmailSender emailSender,
    IOptions<MagicLinkAuthOptions> options,
    TimeProvider timeProvider)
{
    private readonly MagicLinkAuthOptions _options = options.Value;

    public async Task<MagicLinkRequestOutcome> RequestMagicLinkAsync(
        string email,
        string? redirectPath,
        string baseUrl,
        string? requestIp,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var cooldownThreshold = now.AddSeconds(-_options.RequestCooldownSeconds);
        var latestRequestedAtUtc = await dbContext.MagicLinkTokens
            .Where(token => token.Email == normalizedEmail &&
                            token.RequestedAtUtc >= cooldownThreshold)
            .OrderByDescending(token => token.RequestedAtUtc)
            .Select(token => (DateTime?)token.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestRequestedAtUtc is not null)
        {
            return new MagicLinkRequestOutcome(
                false,
                CalculateCooldownSecondsRemaining(latestRequestedAtUtc.Value, now));
        }

        var rawToken = GenerateToken();
        var hashedToken = HashToken(rawToken);
        var sanitizedRedirectPath = SanitizeRedirectPath(redirectPath);
        var expiresAt = now.AddMinutes(_options.TokenLifetimeMinutes);

        var token = new MagicLinkToken
        {
            Email = normalizedEmail,
            TokenHash = hashedToken,
            RedirectPath = sanitizedRedirectPath,
            RequestIp = requestIp,
            RequestedAtUtc = now,
            ExpiresAtUtc = expiresAt
        };

        dbContext.MagicLinkTokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken);

        var magicLink = BuildMagicLink(baseUrl, rawToken);
        try
        {
            await emailSender.SendAsync(normalizedEmail, magicLink, cancellationToken);
        }
        catch
        {
            dbContext.MagicLinkTokens.Remove(token);

            try
            {
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original send failure while best-effort cleanup removes the cooldown token.
            }

            throw;
        }

        return new MagicLinkRequestOutcome(true, _options.RequestCooldownSeconds);
    }

    public async Task<MagicLinkVerificationResult?> VerifyAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var hashedToken = HashToken(token);

        var magicLinkToken = await dbContext.MagicLinkTokens
            .SingleOrDefaultAsync(
                entry => entry.TokenHash == hashedToken &&
                         entry.ConsumedAtUtc == null,
                cancellationToken);

        if (magicLinkToken is null || magicLinkToken.ExpiresAtUtc < now)
        {
            return null;
        }

        magicLinkToken.ConsumedAtUtc = now;

        var user = await dbContext.AuthUsers
            .SingleOrDefaultAsync(entry => entry.Email == magicLinkToken.Email, cancellationToken);

        if (user is null)
        {
            user = new AuthUser
            {
                Email = magicLinkToken.Email,
                DisplayName = BuildDefaultDisplayName(magicLinkToken.Email),
                CreatedAtUtc = now,
                LastLoginAtUtc = now
            };

            dbContext.AuthUsers.Add(user);
        }
        else
        {
            user.LastLoginAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MagicLinkVerificationResult(user, magicLinkToken.RedirectPath ?? "/");
    }

    public static string BuildDefaultDisplayName(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email[..atIndex] : email;
    }

    private static string NormalizeEmail(string email)
    {
        var trimmedEmail = email.Trim();

        try
        {
            var mailAddress = new MailAddress(trimmedEmail);
            return mailAddress.Address.Trim().ToLowerInvariant();
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Bitte eine gueltige Email-Adresse eingeben.");
        }
    }

    private static string GenerateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string SanitizeRedirectPath(string? redirectPath)
    {
        if (string.IsNullOrWhiteSpace(redirectPath))
        {
            return "/";
        }

        return redirectPath.StartsWith("/", StringComparison.Ordinal) &&
               !redirectPath.StartsWith("//", StringComparison.Ordinal)
            ? redirectPath
            : "/";
    }

    private static string BuildMagicLink(string baseUrl, string token)
    {
        var trimmedBaseUrl = baseUrl.TrimEnd('/');
        return $"{trimmedBaseUrl}/auth/magic-link/verify?token={Uri.EscapeDataString(token)}";
    }

    private int CalculateCooldownSecondsRemaining(DateTime requestedAtUtc, DateTime nowUtc)
    {
        var remaining = requestedAtUtc.AddSeconds(_options.RequestCooldownSeconds) - nowUtc;
        return Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
    }
}

public sealed record MagicLinkRequestOutcome(bool EmailSent, int CooldownSecondsRemaining);

public sealed record MagicLinkVerificationResult(AuthUser User, string RedirectPath);