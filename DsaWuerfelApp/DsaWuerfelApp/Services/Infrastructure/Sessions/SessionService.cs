using System.Collections.Concurrent;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public class SessionService
{
    private readonly ConcurrentDictionary<string, string> _codeMap = new();
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new();

    public GameSession CreateSession(string masterUserId, string masterName)
    {
        var session = new GameSession { MasterUserId = masterUserId, JoinCode = GenerateJoinCode() };

        session.Players.Add(new PlayerInfo { UserId = masterUserId, Name = masterName, IsMaster = true });

        _sessions[session.SessionId] = session;
        _codeMap[session.JoinCode] = session.SessionId;

        return session;
    }

    public GameSession? GetByCode(string code)
    {
        if (_codeMap.TryGetValue(code.ToUpper(), out var sessionId))
        {
            return _sessions.GetValueOrDefault(sessionId);
        }

        return null;
    }

    public GameSession? GetById(string sessionId)
        => _sessions.GetValueOrDefault(sessionId);

    public void AddPlayer(string sessionId, PlayerInfo player)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            var existing = session.Players.FirstOrDefault(p => p.UserId == player.UserId);
            if (existing != null)
            {
                existing.ConnectionId = player.ConnectionId;
            }
            else
            {
                session.Players.Add(player);
            }
        }
    }

    private static string GenerateJoinCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 4).Select(s => s[random.Next(s.Length)]).ToArray());
    }
}