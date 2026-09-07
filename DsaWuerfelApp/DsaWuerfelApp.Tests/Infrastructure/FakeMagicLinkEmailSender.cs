using DsaWuerfelApp.Services.Auth;

namespace DsaWuerfelApp.Tests.Infrastructure;

public sealed record SentMagicLink(string Email, string Link);

public sealed class FakeMagicLinkEmailSender : IMagicLinkEmailSender
{
    public List<SentMagicLink> SentMessages { get; } = [];

    public Task SendAsync(string email, string magicLink, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(new SentMagicLink(email, magicLink));
        return Task.CompletedTask;
    }
}
