namespace DsaWuerfelApp.Shared;

public sealed record MagicLinkRequestDto(string Email, string? RedirectPath);

public sealed record MagicLinkRequestResultDto(
    string Message,
    bool EmailSent,
    int CooldownSecondsRemaining);

public sealed record AuthUserDto(string Id, string Email, string DisplayName);

public sealed record AuthSessionDto(bool IsAuthenticated, AuthUserDto? User);