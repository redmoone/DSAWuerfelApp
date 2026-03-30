using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.SignalR;

namespace DsaWuerfelApp.Hubs;

public class GameHub(
    SessionService sessionService,
    DiceWorkflowService diceWorkflowService)
    : Hub
{
    public async Task<SessionConnectionDto> CreateSession(string userId, string userName)
    {
        var session = sessionService.CreateSession(userId, userName);
        sessionService.AddPlayer(
            session.SessionId,
            new PlayerInfo { ConnectionId = Context.ConnectionId, UserId = userId, Name = userName, IsMaster = true });

        await Groups.AddToGroupAsync(Context.ConnectionId, session.SessionId);

        return new SessionConnectionDto(session.SessionId, session.JoinCode);
    }

    public async Task<SessionConnectionDto?> JoinSession(string code, string userId, string userName)
    {
        var session = sessionService.GetByCode(code);
        if (session is null)
        {
            return null;
        }

        sessionService.AddPlayer(
            session.SessionId,
            new PlayerInfo { ConnectionId = Context.ConnectionId, UserId = userId, Name = userName, IsMaster = false });

        await Groups.AddToGroupAsync(Context.ConnectionId, session.SessionId);
        await Clients.Group(session.SessionId).SendAsync("PlayerJoined", userName);

        return new SessionConnectionDto(session.SessionId, session.JoinCode);
    }

    public async Task RollFree(FreeRollRequestDto request)
    {
        var session = GetSession(request.SessionId);
        if (session is null)
        {
            return;
        }

        var result = diceWorkflowService.RollFree(request, ResolvePlayerName(session));
        session.History.Add(result.HistoryEntry);

        await Clients.Group(session.SessionId).SendAsync("ShowFreeRollResult", result);
    }

    public async Task RollTalent(TalentRollRequestDto request)
    {
        var session = GetSession(request.SessionId);
        if (session is null)
        {
            return;
        }

        var result = await diceWorkflowService.RollTalentAsync(request, ResolvePlayerName(session));
        session.History.Add(result.HistoryEntry);

        await Clients.Group(session.SessionId).SendAsync("ShowTalentRollResult", result);
    }

    public async Task RollAttribute(AttributeRollRequestDto request)
    {
        var session = GetSession(request.SessionId);
        if (session is null)
        {
            return;
        }

        var result = await diceWorkflowService.RollAttributeAsync(request, ResolvePlayerName(session));
        session.History.Add(result.HistoryEntry);

        await Clients.Group(session.SessionId).SendAsync("ShowAttributeRollResult", result);
    }

    public async Task RollBadTrait(BadTraitRollRequestDto request)
    {
        var session = GetSession(request.SessionId);
        if (session is null)
        {
            return;
        }

        var result = await diceWorkflowService.RollBadTraitAsync(request, ResolvePlayerName(session));
        session.History.Add(result.HistoryEntry);

        await Clients.Group(session.SessionId).SendAsync("ShowBadTraitRollResult", result);
    }

    private GameSession? GetSession(string? sessionId)
    {
        return string.IsNullOrWhiteSpace(sessionId)
            ? null
            : sessionService.GetById(sessionId);
    }

    private string ResolvePlayerName(GameSession session)
    {
        return session.Players.FirstOrDefault(player => player.ConnectionId == Context.ConnectionId)?.Name ?? "Jemand";
    }
}