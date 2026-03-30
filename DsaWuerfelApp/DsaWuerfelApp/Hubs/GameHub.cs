using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.SignalR;

namespace DsaWuerfelApp.Hubs;

public class GameHub(
    SessionService sessionService,
    DiceService diceService,
    TalentProbeService talentProbeService,
    SchlechteEigenschaftProbeService schlechteEigenschaftProbeService)
    : Hub
{
    public async Task<string> CreateSession(string userId, string userName)
    {
        var session = sessionService.CreateSession(userId, userName);
        await Groups.AddToGroupAsync(Context.ConnectionId, session.SessionId);
        return session.JoinCode;
    }

    public async Task<bool> JoinSession(string code, string userId, string userName)
    {
        var session = sessionService.GetByCode(code);
        if (session == null) return false;

        sessionService.AddPlayer(session.SessionId,
            new PlayerInfo { ConnectionId = Context.ConnectionId, UserId = userId, Name = userName, IsMaster = false });

        await Groups.AddToGroupAsync(Context.ConnectionId, session.SessionId);
        await Clients.Group(session.SessionId).SendAsync("PlayerJoined", userName);

        return true;
    }

    public async Task RollDice(RollRequest request)
    {
        var session = sessionService.GetById(request.SessionId);
        if (session == null) return;

        var player = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
        var playerName = player?.Name ?? "Jemand";

        var result = diceService.RollSet(request.Dice, request.Modifier, playerName);

        session.History.Add(result);

        await Clients.Group(request.SessionId).SendAsync("ShowRollResult", result);
    }

    public async Task RollTalentProbe(TalentProbeRequest request)
    {
        var session = sessionService.GetById(request.SessionId);
        if (session == null) return;

        var player = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
        var playerName = player?.Name ?? "Jemand";

        var result = talentProbeService.RollTalentProbe(request, playerName);
        session.History.Add(TalentProbeService.ToRollResult(result));

        await Clients.Group(request.SessionId).SendAsync("ShowTalentProbeResult", result);
    }

    public async Task RollSchlechteEigenschaftProbe(SchlechteEigenschaftProbeRequest request)
    {
        var session = sessionService.GetById(request.SessionId);
        if (session == null) return;

        var player = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
        var playerName = player?.Name ?? "Jemand";

        var result = schlechteEigenschaftProbeService.RollProbe(request, playerName);
        session.History.Add(SchlechteEigenschaftProbeService.ToRollResult(result));

        await Clients.Group(request.SessionId).SendAsync("ShowSchlechteEigenschaftProbeResult", result);
    }
}