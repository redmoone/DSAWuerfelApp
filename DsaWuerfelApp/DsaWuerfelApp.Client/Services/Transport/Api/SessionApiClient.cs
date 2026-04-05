using System.Net.Http.Json;

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
        await response.EnsureApiSuccessAsync("Sessions konnten nicht geladen werden.", cancellationToken);

        var sessions = await response.Content.ReadFromJsonAsync<SessionSummaryDto[]>(cancellationToken);
        return sessions ?? Array.Empty<SessionSummaryDto>();
    }

    public async Task<SessionDetailsDto> GetSessionAsync(string sessionId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await httpClient.GetAsync($"api/sessions/{Uri.EscapeDataString(sessionId)}", cancellationToken);
        await response.EnsureApiSuccessAsync("Session konnte nicht geladen werden.", cancellationToken);

        var session = await response.Content.ReadFromJsonAsync<SessionDetailsDto>(cancellationToken);
        return session ?? throw new InvalidOperationException("Session konnte nicht geladen werden.");
    }
}