namespace DsaWuerfelApp.Shared;

public sealed record MagicLinkRequestDto(string Email, string? RedirectPath);

public sealed record MagicLinkRequestResultDto(string Message);

public sealed record AuthUserDto(string Id, string Email, string DisplayName);

public sealed record AuthSessionDto(bool IsAuthenticated, AuthUserDto? User);