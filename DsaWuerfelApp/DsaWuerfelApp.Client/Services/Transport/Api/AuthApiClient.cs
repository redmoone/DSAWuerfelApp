using System.Net.Http.Json;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public interface IAuthApiClient
{
    Task<AuthSessionDto> GetSessionAsync(CancellationToken cancellationToken = default);

    Task<MagicLinkRequestResultDto> RequestMagicLinkAsync(
        MagicLinkRequestDto request,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthApiClient(HttpClient httpClient) : IAuthApiClient
{
    public Task<AuthSessionDto> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<AuthSessionDto>("api/auth/me", "Authentifizierungsstatus konnte nicht geladen werden.",
            cancellationToken);
    }

    public Task<MagicLinkRequestResultDto> RequestMagicLinkAsync(
        MagicLinkRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<MagicLinkRequestResultDto>(
            "api/auth/magic-link/request",
            request,
            "Magic Link konnte nicht angefordert werden.",
            cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync("api/auth/logout", content: null, cancellationToken);
        await response.EnsureApiSuccessAsync("Logout konnte nicht ausgefuehrt werden.", cancellationToken);
    }

    private async Task<T> GetJsonAsync<T>(string uri, string fallbackMessage, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        await response.EnsureApiSuccessAsync(fallbackMessage, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ??
               throw new InvalidOperationException(fallbackMessage);
    }

    private async Task<T> PostJsonAsync<T>(
        string uri,
        object request,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(uri, request, cancellationToken);
        await response.EnsureApiSuccessAsync(fallbackMessage, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ??
               throw new InvalidOperationException(fallbackMessage);
    }
}