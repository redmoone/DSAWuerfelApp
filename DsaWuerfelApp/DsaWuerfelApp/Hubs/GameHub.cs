using Microsoft.AspNetCore.SignalR;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Services;

namespace DsaWuerfelApp.Hubs;

public class GameHub : Hub
{
    private readonly SessionService _sessionService;
    private readonly DiceService _diceService; // Dein existierender Service

    public GameHub(SessionService sessionService, DiceService diceService)
    {
        _sessionService = sessionService;
        _diceService = diceService;
    }

    public async Task<string> CreateSession(string userId, string userName)
    {
        var session = _sessionService.CreateSession(userId, userName);
        
        // Den Ersteller (Meister) in die SignalR-Gruppe packen
        await Groups.AddToGroupAsync(Context.ConnectionId, session.SessionId);
        
        return session.JoinCode;
    }

    public async Task<bool> JoinSession(string code, string userId, string userName)
    {
        var session = _sessionService.GetByCode(code);
        if (session == null) return false;

        _sessionService.AddPlayer(session.SessionId, new PlayerInfo
        {
            ConnectionId = Context.ConnectionId,
            UserId = userId,
            Name = userName,
            IsMaster = false
        });

        await Groups.AddToGroupAsync(Context.ConnectionId, session.SessionId);
        
        // Alle anderen informieren
        await Clients.Group(session.SessionId).SendAsync("PlayerJoined", userName);
        
        return true;
    }

    public async Task RollDice(RollRequest request)
    {
        // Convert Shared.DiceGroup to Services.DiceGroup
        var serviceDiceGroups = request.Dice.Select(d => new DsaWuerfelApp.Services.DiceGroup(d.Sides, d.Count)).ToList();

        var resultData = _diceService.RollSet(serviceDiceGroups, request.Modifier);
        
        var result = new RollResult
        {
            // TODO: Usernamen aus Session holen
            PlayerName = "Jemand", 
            TotalSum = resultData.Total,
            Modifier = request.Modifier,
            Rolls = resultData.Rolls.Select(r => new DsaWuerfelApp.Shared.SingleRoll { Sides = r.Sides, Value = r.Value }).ToList()
        };

        // 2. An ALLE in der Gruppe senden
        await Clients.Group(request.SessionId).SendAsync("ShowRollResult", result);
    }
}