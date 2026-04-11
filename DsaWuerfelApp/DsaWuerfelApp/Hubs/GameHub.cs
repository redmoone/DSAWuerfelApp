using System.Security.Claims;

using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DsaWuerfelApp.Hubs;

[Authorize]
public class GameHub(
    SessionService sessionService,
    DiceWorkflowService diceWorkflowService,
    GameSessionRollPipeline gameSessionRollPipeline)
    : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = GetRequiredUserId();
        var affectedUserIds = sessionService.RegisterConnection(userId, Context.ConnectionId);

        await base.OnConnectedAsync();
        await NotifySessionsChangedAsync(affectedUserIds);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var affectedUserIds = string.IsNullOrWhiteSpace(userId)
            ? Array.Empty<string>()
            : sessionService.UnregisterConnection(Context.ConnectionId);

        await NotifySessionsChangedAsync(affectedUserIds);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<SessionConnectionDto> CreateSession(string? userName, string? sessionName)
    {
        var userId = GetRequiredUserId();
        var resolvedUserName = ResolveUserName(userName);
        var session = sessionService.CreateSession(userId, resolvedUserName, sessionName);

        await ActivateSessionAsync(session.SessionId, userId);
        await NotifySessionsChangedAsync(session.Players.Select(player => player.UserId));

        return new SessionConnectionDto(session.SessionId, session.JoinCode);
    }

    public async Task<SessionConnectionDto?> JoinSession(string code, string? userName)
    {
        var userId = GetRequiredUserId();
        var resolvedUserName = ResolveUserName(userName);
        var session = sessionService.GetByCode(code);
        if (session is null)
        {
            return null;
        }

        sessionService.AddPlayer(
            session.SessionId,
            new PlayerInfo { UserId = userId, Name = resolvedUserName, IsMaster = false });

        await ActivateSessionAsync(session.SessionId, userId);
        await Clients.Group(session.SessionId).SendAsync("PlayerJoined", resolvedUserName);
        await NotifySessionsChangedAsync(session.Players.Select(player => player.UserId));

        return new SessionConnectionDto(session.SessionId, session.JoinCode);
    }

    public async Task<SessionConnectionDto> OpenSession(string sessionId)
    {
        var userId = GetRequiredUserId();
        GameSession session;

        try
        {
            session = sessionService.OpenSession(sessionId, userId);
        }
        catch (InvalidOperationException exception)
        {
            throw new HubException(exception.Message);
        }

        await ActivateSessionAsync(session.SessionId, userId);
        return new SessionConnectionDto(session.SessionId, session.JoinCode);
    }

    public async Task LeaveSession(string sessionId)
    {
        var userId = GetRequiredUserId();
        LeaveSessionResult result;

        try
        {
            result = sessionService.LeaveSession(sessionId, userId);
        }
        catch (InvalidOperationException exception)
        {
            throw new HubException(exception.Message);
        }

        foreach (var connectionId in result.DetachedConnectionIds)
        {
            await Groups.RemoveFromGroupAsync(connectionId, sessionId);
        }

        await Clients.User(userId).SendAsync("SessionClosed", sessionId);
        await NotifySessionsChangedAsync(result.AffectedUserIds);
    }

    public Task<SessionSummaryDto[]> GetMySessions()
    {
        var userId = GetRequiredUserId();
        var sessions = sessionService.GetSessionsForUser(userId).ToArray();
        return Task.FromResult(sessions);
    }

    public async Task RenameSession(string sessionId, string? sessionName)
    {
        var userId = GetRequiredUserId();
        GameSession session;

        try
        {
            session = sessionService.RenameSession(sessionId, userId, sessionName);
        }
        catch (InvalidOperationException exception)
        {
            throw new HubException(exception.Message);
        }

        await Clients.Users(session.Players.Select(player => player.UserId)).SendAsync(
            "SessionRenamed",
            session.SessionId,
            session.Name);
        await NotifySessionsChangedAsync(session.Players.Select(player => player.UserId));
    }

    public async Task RenamePlayer(string sessionId, string? playerName)
    {
        var userId = GetRequiredUserId();
        GameSession session;

        try
        {
            session = sessionService.RenamePlayer(sessionId, userId, playerName);
        }
        catch (InvalidOperationException exception)
        {
            throw new HubException(exception.Message);
        }

        await NotifySessionsChangedAsync(session.Players.Select(player => player.UserId));
    }

    public async Task DeleteSession(string sessionId)
    {
        var userId = GetRequiredUserId();
        IReadOnlyList<string> affectedUserIds;

        try
        {
            affectedUserIds = sessionService.DeleteSession(sessionId, userId);
        }
        catch (InvalidOperationException exception)
        {
            throw new HubException(exception.Message);
        }

        await Clients.Users(affectedUserIds).SendAsync("SessionClosed", sessionId);
        await NotifySessionsChangedAsync(affectedUserIds);
    }

    public async Task UpdateActiveHero(string sessionId, Guid? heroId, string? heroName)
    {
        var userId = GetRequiredUserId();
        IReadOnlyList<string> affectedUserIds;

        try
        {
            affectedUserIds = sessionService.UpdatePlayerHero(sessionId, userId, heroId, heroName);
        }
        catch (InvalidOperationException exception)
        {
            throw new HubException(exception.Message);
        }

        await NotifySessionsChangedAsync(affectedUserIds);
    }

    public Task RollFree(FreeRollRequestDto request)
    {
        return ExecuteRollAsync(
            request.SessionId,
            playerName => Task.FromResult(diceWorkflowService.RollFree(request, playerName)),
            result => result.HistoryEntry,
            (sessionId, result) => Clients.Group(sessionId).SendAsync("ShowFreeRollResult", result));
    }

    public Task RollTalent(TalentRollRequestDto request)
    {
        return ExecuteRollAsync(
            request.SessionId,
            playerName => diceWorkflowService.RollTalentAsync(request, playerName, Context.ConnectionAborted),
            result => result.HistoryEntry,
            (sessionId, result) => Clients.Group(sessionId).SendAsync("ShowTalentRollResult", result));
    }

    public Task RollAttribute(AttributeRollRequestDto request)
    {
        return ExecuteRollAsync(
            request.SessionId,
            playerName => diceWorkflowService.RollAttributeAsync(request, playerName, Context.ConnectionAborted),
            result => result.HistoryEntry,
            (sessionId, result) => Clients.Group(sessionId).SendAsync("ShowAttributeRollResult", result));
    }

    public Task RollBadTrait(BadTraitRollRequestDto request)
    {
        return ExecuteRollAsync(
            request.SessionId,
            playerName => diceWorkflowService.RollBadTraitAsync(request, playerName, Context.ConnectionAborted),
            result => result.HistoryEntry,
            (sessionId, result) => Clients.Group(sessionId).SendAsync("ShowBadTraitRollResult", result));
    }

    private async Task ExecuteRollAsync<TResult>(
        string? sessionId,
        Func<string, Task<TResult>> executeAsync,
        Func<TResult, RollHistoryEntryDto> historyEntrySelector,
        Func<string, TResult, Task> broadcastAsync)
    {
        try
        {
            await gameSessionRollPipeline.ExecuteAsync(
                sessionId,
                Context.ConnectionId,
                GetRequiredUserId(),
                executeAsync,
                historyEntrySelector,
                broadcastAsync);
        }
        catch (InvalidOperationException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    private async Task ActivateSessionAsync(string sessionId, string userId)
    {
        var previousSessionId = sessionService.ActivateSessionConnection(sessionId, userId, Context.ConnectionId);
        if (!string.IsNullOrWhiteSpace(previousSessionId) &&
            !string.Equals(previousSessionId, sessionId, StringComparison.Ordinal))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, previousSessionId);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }

    private string GetRequiredUserId()
    {
        return Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ??
               throw new HubException("Benutzer ist nicht authentifiziert.");
    }

    private string ResolveUserName(string? requestedUserName)
    {
        if (!string.IsNullOrWhiteSpace(requestedUserName))
        {
            return requestedUserName.Trim();
        }

        return Context.User?.Identity?.Name ??
               Context.User?.FindFirstValue(ClaimTypes.Email) ??
               "Unbekannt";
    }

    private Task NotifySessionsChangedAsync(IEnumerable<string> userIds)
    {
        var distinctUserIds = userIds
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinctUserIds.Length == 0)
        {
            return Task.CompletedTask;
        }

        return Clients.Users(distinctUserIds).SendAsync("SessionsChanged");
    }
}
