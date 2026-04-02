using System.Net.Http.Json;
using System.Text.Json;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public interface ISessionApiClient
{
    Task<IReadOnlyList<SessionSummaryDto>> GetMySessionsAsync(CancellationToken cancellationToken = default);
    Task<SessionDetailsDto> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}

public sealed class SessionApiClient(HttpClient httpClient) : ISessionApiClient
{
    public async Task<IReadOnlyList<SessionSummaryDto>> GetMySessionsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/sessions/mine", cancellationToken);
        await EnsureSuccessAsync(response, "Sessions konnten nicht geladen werden.", cancellationToken);

        var sessions = await response.Content.ReadFromJsonAsync<SessionSummaryDto[]>(cancellationToken);
        return sessions ?? Array.Empty<SessionSummaryDto>();
    }

    public async Task<SessionDetailsDto> GetSessionAsync(string sessionId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await httpClient.GetAsync($"api/sessions/{Uri.EscapeDataString(sessionId)}", cancellationToken);
        await EnsureSuccessAsync(response, "Session konnte nicht geladen werden.", cancellationToken);

        var session = await response.Content.ReadFromJsonAsync<SessionDetailsDto>(cancellationToken);
        return session ?? throw new InvalidOperationException("Session konnte nicht geladen werden.");
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