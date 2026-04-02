using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class GameSessionRollPipeline(SessionService sessionService)
{
    public async Task ExecuteAsync<TResult>(
        string? sessionId,
        string connectionId,
        string userId,
        Func<string, Task<TResult>> executeAsync,
        Func<TResult, RollHistoryEntryDto> historyEntrySelector,
        Func<string, TResult, Task> broadcastAsync)
    {
        var session = sessionService.RequireRollSession(sessionId, connectionId, userId);

        var result = await executeAsync(sessionService.ResolvePlayerName(session.SessionId, userId));
        sessionService.AppendHistoryEntry(session.SessionId, historyEntrySelector(result));
        await broadcastAsync(session.SessionId, result);
    }
}