using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.SignalR;

namespace DsaWuerfelApp.Hubs;

public class GameHub(
    SessionService sessionService,
    DiceWorkflowService diceWorkflowService,
    GameSessionRollPipeline gameSessionRollPipeline)
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
        await gameSessionRollPipeline.ExecuteAsync(
            request.SessionId,
            Context.ConnectionId,
            playerName => Task.FromResult(diceWorkflowService.RollFree(request, playerName)),
            result => result.HistoryEntry,
            (sessionId, result) => Clients.Group(sessionId).SendAsync("ShowFreeRollResult", result));
    }

    public async Task RollTalent(TalentRollRequestDto request)
    {
        await gameSessionRollPipeline.ExecuteAsync(
            request.SessionId,
            Context.ConnectionId,
            playerName => diceWorkflowService.RollTalentAsync(request, playerName, Context.ConnectionAborted),
            result => result.HistoryEntry,
            (sessionId, result) => Clients.Group(sessionId).SendAsync("ShowTalentRollResult", result));
    }

    public async Task RollAttribute(AttributeRollRequestDto request)
    {
        await gameSessionRollPipeline.ExecuteAsync(
            request.SessionId,
            Context.ConnectionId,
            playerName => diceWorkflowService.RollAttributeAsync(request, playerName, Context.ConnectionAborted),
            result => result.HistoryEntry,
            (sessionId, result) => Clients.Group(sessionId).SendAsync("ShowAttributeRollResult", result));
    }

    public async Task RollBadTrait(BadTraitRollRequestDto request)
    {
        await gameSessionRollPipeline.ExecuteAsync(
            request.SessionId,
            Context.ConnectionId,
            playerName => diceWorkflowService.RollBadTraitAsync(request, playerName, Context.ConnectionAborted),
            result => result.HistoryEntry,
            (sessionId, result) => Clients.Group(sessionId).SendAsync("ShowBadTraitRollResult", result));
    }
}