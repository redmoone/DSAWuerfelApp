using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class GameSessionRollPipeline(SessionService sessionService)
{
    public async Task ExecuteAsync<TResult>(
        string? sessionId,
        string connectionId,
        Func<string, Task<TResult>> executeAsync,
        Func<TResult, RollHistoryEntryDto> historyEntrySelector,
        Func<string, TResult, Task> broadcastAsync)
    {
        var session = ResolveSession(sessionId);
        if (session is null)
        {
            return;
        }

        var result = await executeAsync(ResolvePlayerName(session, connectionId));
        session.History.Add(historyEntrySelector(result));
        await broadcastAsync(session.SessionId, result);
    }

    private GameSession? ResolveSession(string? sessionId)
    {
        return string.IsNullOrWhiteSpace(sessionId)
            ? null
            : sessionService.GetById(sessionId);
    }

    private static string ResolvePlayerName(GameSession session, string connectionId)
    {
        return session.Players.FirstOrDefault(player => player.ConnectionId == connectionId)?.Name ?? "Jemand";
    }
}