using System.Net.Http.Json;
using System.Text.Json;

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
        await EnsureSuccessAsync(response, "Logout konnte nicht ausgefuehrt werden.", cancellationToken);
    }

    private async Task<T> GetJsonAsync<T>(string uri, string fallbackMessage, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        await EnsureSuccessAsync(response, fallbackMessage, cancellationToken);

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
        await EnsureSuccessAsync(response, fallbackMessage, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ??
               throw new InvalidOperationException(fallbackMessage);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var apiError = await TryReadApiErrorAsync(response, cancellationToken);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(apiError) ? fallbackMessage : apiError);
    }

    private static async Task<string?> TryReadApiErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var apiError =
                JsonSerializer.Deserialize<ApiError>(content, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return string.IsNullOrWhiteSpace(apiError?.Error) ? content : apiError.Error;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ApiError(string? Error);
}