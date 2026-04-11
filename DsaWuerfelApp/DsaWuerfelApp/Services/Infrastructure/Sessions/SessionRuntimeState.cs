using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class SessionRuntimeState
{
    private readonly Dictionary<string, string> _activeSessionByConnection = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _codeMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _connectionsByUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GameSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _userByConnection = new(StringComparer.Ordinal);

    public bool IsInitialized { get; private set; }

    public void Initialize(IEnumerable<GameSession> sessions)
    {
        _sessions.Clear();
        _codeMap.Clear();

        foreach (var session in sessions)
        {
            AddSession(session);
        }

        IsInitialized = true;
    }

    public GameSession? GetByCode(string code)
    {
        return _codeMap.TryGetValue(NormalizeJoinCode(code), out var sessionId)
            ? _sessions.GetValueOrDefault(sessionId)
            : null;
    }

    public bool TryGetSession(string sessionId, out GameSession session)
    {
        return _sessions.TryGetValue(sessionId, out session!);
    }

    public GameSession GetRequiredSession(string sessionId)
    {
        return _sessions.GetValueOrDefault(sessionId) ??
               throw new InvalidOperationException("Session wurde nicht gefunden.");
    }

    public GameSession GetMemberSession(string sessionId, string userId)
    {
        var session = GetRequiredSession(sessionId);
        EnsureMember(session, userId);
        return session;
    }

    public bool ContainsSession(string sessionId) => _sessions.ContainsKey(sessionId);

    public bool ContainsJoinCode(string joinCode) => _codeMap.ContainsKey(joinCode);

    public void AddSession(GameSession session)
    {
        _sessions[session.SessionId] = session;
        _codeMap[session.JoinCode] = session.SessionId;
    }

    public void RemoveSession(string sessionId)
    {
        var session = GetRequiredSession(sessionId);
        _sessions.Remove(session.SessionId);
        _codeMap.Remove(session.JoinCode);
    }

    public void UpsertPlayer(GameSession session, PlayerInfo player)
    {
        var existing = FindPlayer(session, player.UserId);
        if (existing is null)
        {
            session.Players = [.. session.Players, player];
            return;
        }

        existing.Name = player.Name;
        existing.AvatarUrl = player.AvatarUrl;
        existing.IsMaster |= player.IsMaster;
    }

    public string? ActivateSessionConnection(string sessionId, string userId, string connectionId)
    {
        GetMemberSession(sessionId, userId);

        var previousSessionId = _activeSessionByConnection.GetValueOrDefault(connectionId);
        _activeSessionByConnection[connectionId] = sessionId;
        return previousSessionId;
    }

    public SessionDetailsDto BuildSessionDetails(GameSession session, RollHistoryEntryDto[] history)
    {
        return new SessionDetailsDto(
            session.SessionId,
            session.Name,
            session.JoinCode,
            session.MasterUserId,
            BuildPlayers(session),
            history);
    }

    public void ReplacePlayers(GameSession session, string masterUserId, PlayerInfo[] players)
    {
        session.MasterUserId = masterUserId;
        session.Players = players;
    }

    public void RenamePlayer(GameSession session, string userId, string playerName)
    {
        var player = FindPlayer(session, userId);
        if (player is not null)
        {
            player.Name = playerName;
        }
    }

    public void UpdatePlayerHero(GameSession session, string userId, Guid? heroId, string? heroName)
    {
        var player = FindPlayer(session, userId);
        if (player is null)
        {
            return;
        }

        player.ActiveHeroId = heroId;
        player.ActiveHeroName = string.IsNullOrWhiteSpace(heroName) ? null : heroName.Trim();
    }

    public IReadOnlyList<SessionSummaryDto> GetSessionsForUser(string userId)
    {
        return _sessions.Values
            .Where(session => FindPlayer(session, userId) is not null)
            .OrderBy(session => session.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.CreatedAt)
            .Select(ToSummary)
            .ToArray();
    }

    public IReadOnlyList<string> GetAffectedUserIds(GameSession session)
    {
        return session.Players
            .Select(player => player.UserId)
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> RegisterConnection(string userId, string connectionId)
    {
        _userByConnection[connectionId] = userId;

        if (!_connectionsByUser.TryGetValue(userId, out var connections))
        {
            connections = new HashSet<string>(StringComparer.Ordinal);
            _connectionsByUser[userId] = connections;
        }

        var wasOnline = connections.Count > 0;
        connections.Add(connectionId);

        return wasOnline
            ? Array.Empty<string>()
            : GetPresenceAffectedUserIds(userId);
    }

    public IReadOnlyList<string> UnregisterConnection(string connectionId)
    {
        _activeSessionByConnection.Remove(connectionId);

        if (!_userByConnection.Remove(connectionId, out var userId))
        {
            return Array.Empty<string>();
        }

        if (!_connectionsByUser.TryGetValue(userId, out var connections))
        {
            return Array.Empty<string>();
        }

        connections.Remove(connectionId);
        if (connections.Count > 0)
        {
            return Array.Empty<string>();
        }

        _connectionsByUser.Remove(userId);
        return GetPresenceAffectedUserIds(userId);
    }

    public string ResolvePlayerName(string sessionId, string userId)
    {
        return GetMemberSession(sessionId, userId).Players
            .FirstOrDefault(player => string.Equals(player.UserId, userId, StringComparison.Ordinal))?.Name ?? "Jemand";
    }

    public GameSession RequireRollSession(string? sessionId, string connectionId, string userId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Für diesen Wurf ist keine aktive Session ausgewählt.");
        }

        var session = GetMemberSession(sessionId, userId);
        if (!_activeSessionByConnection.TryGetValue(connectionId, out var activeSessionId) ||
            !string.Equals(activeSessionId, sessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Diese Session ist aktuell nicht geöffnet.");
        }

        return session;
    }

    public void RemoveActiveSessionMappingsForSession(string sessionId)
    {
        var connectionIds = _activeSessionByConnection
            .Where(current => string.Equals(current.Value, sessionId, StringComparison.Ordinal))
            .Select(current => current.Key)
            .ToArray();

        foreach (var connectionId in connectionIds)
        {
            _activeSessionByConnection.Remove(connectionId);
        }
    }

    public void RemoveActiveSessionMappingsForUserInSession(string userId, string sessionId)
    {
        foreach (var connectionId in GetConnectionIdsForUserInSession(userId, sessionId))
        {
            _activeSessionByConnection.Remove(connectionId);
        }
    }

    public string[] GetConnectionIdsForUserInSession(string userId, string sessionId)
    {
        return _activeSessionByConnection
            .Where(current =>
                string.Equals(current.Value, sessionId, StringComparison.Ordinal) &&
                string.Equals(_userByConnection.GetValueOrDefault(current.Key), userId, StringComparison.Ordinal))
            .Select(current => current.Key)
            .ToArray();
    }

    private IReadOnlyList<string> GetPresenceAffectedUserIds(string userId)
    {
        return _sessions.Values
            .Where(session => FindPlayer(session, userId) is not null)
            .SelectMany(session => session.Players.Select(player => player.UserId))
            .Where(current => !string.IsNullOrWhiteSpace(current))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private SessionSummaryDto ToSummary(GameSession session)
    {
        return new SessionSummaryDto(
            session.SessionId,
            session.Name,
            session.JoinCode,
            session.MasterUserId,
            BuildPlayers(session));
    }

    private SessionPlayerDto[] BuildPlayers(GameSession session)
    {
        return session.Players
            .OrderByDescending(player => player.IsMaster)
            .ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .Select(player => new SessionPlayerDto(
                player.UserId,
                player.Name,
                player.IsMaster,
                IsUserOnline(player.UserId),
                player.ActiveHeroId,
                player.ActiveHeroName))
            .ToArray();
    }

    private bool IsUserOnline(string userId)
    {
        return !string.IsNullOrWhiteSpace(userId) &&
               _connectionsByUser.TryGetValue(userId, out var connections) &&
               connections.Count > 0;
    }

    private static PlayerInfo? FindPlayer(GameSession session, string userId)
    {
        return session.Players.FirstOrDefault(player => string.Equals(player.UserId, userId, StringComparison.Ordinal));
    }

    private static void EnsureMember(GameSession session, string userId)
    {
        if (FindPlayer(session, userId) is null)
        {
            throw new InvalidOperationException("Spieler ist nicht Teil dieser Session.");
        }
    }

    private static string NormalizeJoinCode(string code)
    {
        return code.Trim().ToUpperInvariant();
    }
}
