using System.Collections.Concurrent;
using System.Text.Json;

using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared;

using Microsoft.EntityFrameworkCore;

namespace DsaWuerfelApp.Services;

public class SessionService
{
    private const int MaxPersistedHistoryEntries = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, string> _activeSessionByConnection = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, string> _codeMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _connectionsByUser = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, string> _userByConnection = new(StringComparer.Ordinal);
    private bool _initialized;

    public SessionService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public GameSession CreateSession(string masterUserId, string masterName, string? sessionName)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
            var joinCode = GenerateUniqueJoinCode(dbContext);

            var record = new SessionRecord
            {
                Id = Guid.NewGuid().ToString(),
                MasterUserId = masterUserId,
                JoinCode = joinCode,
                Name = ResolveSessionName(sessionName),
                CreatedAtUtc = DateTime.UtcNow,
                Participants =
                [
                    new SessionParticipantRecord { UserId = masterUserId, Name = masterName, IsMaster = true }
                ]
            };

            dbContext.SessionRecords.Add(record);
            dbContext.SaveChanges();

            var session = ToGameSession(record);
            _sessions[session.SessionId] = session;
            _codeMap[session.JoinCode] = session.SessionId;

            return session;
        }
    }

    public GameSession? GetByCode(string code)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            if (_codeMap.TryGetValue(NormalizeJoinCode(code), out var sessionId))
            {
                return _sessions.GetValueOrDefault(sessionId);
            }

            return null;
        }
    }

    public GameSession? GetById(string sessionId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();
            return _sessions.GetValueOrDefault(sessionId);
        }
    }

    public GameSession OpenSession(string sessionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();
            var session = GetRequiredSession(sessionId);
            EnsureMember(session, userId);
            return session;
        }
    }

    public void AddPlayer(string sessionId, PlayerInfo player)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
            var record = dbContext.SessionRecords
                .Include(current => current.Participants)
                .SingleOrDefault(current => current.Id == sessionId);

            if (record is null)
            {
                return;
            }

            var participant = record.Participants
                .FirstOrDefault(current => string.Equals(current.UserId, player.UserId, StringComparison.Ordinal));

            if (participant is null)
            {
                record.Participants.Add(ToParticipantRecord(sessionId, player));
            }
            else
            {
                participant.Name = player.Name;
                participant.AvatarUrl = player.AvatarUrl;
                participant.IsMaster = participant.IsMaster || player.IsMaster;
            }

            dbContext.SaveChanges();
            UpsertMemoryPlayer(session, player);
        }
    }

    public string? ActivateSessionConnection(string sessionId, string userId, string connectionId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = GetRequiredSession(sessionId);
            EnsureMember(session, userId);

            var previousSessionId = _activeSessionByConnection.GetValueOrDefault(connectionId);
            _activeSessionByConnection[connectionId] = sessionId;
            return previousSessionId;
        }
    }

    public SessionDetailsDto GetSessionDetails(string sessionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = GetRequiredSession(sessionId);
            EnsureMember(session, userId);

            var history = LoadPersistedHistory(sessionId);
            var players = BuildPlayers(session);

            return new SessionDetailsDto(
                session.SessionId,
                session.Name,
                session.JoinCode,
                session.MasterUserId,
                players,
                history);
        }
    }

    public LeaveSessionResult LeaveSession(string sessionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = GetRequiredSession(sessionId);
            EnsureMember(session, userId);

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
            var record = dbContext.SessionRecords
                             .Include(current => current.Participants)
                             .SingleOrDefault(current => current.Id == sessionId) ??
                         throw new InvalidOperationException("Session wurde nicht gefunden.");

            var participant = record.Participants
                                  .FirstOrDefault(current =>
                                      string.Equals(current.UserId, userId, StringComparison.Ordinal)) ??
                              throw new InvalidOperationException("Spieler ist nicht Teil dieser Session.");

            var remainingParticipants = record.Participants
                .Where(current => !string.Equals(current.UserId, userId, StringComparison.Ordinal))
                .OrderByDescending(current => current.IsMaster)
                .ThenBy(current => current.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(current => current.UserId, StringComparer.Ordinal)
                .ToArray();

            var affectedUserIds = remainingParticipants
                .Select(current => current.UserId)
                .Append(userId)
                .Where(current => !string.IsNullOrWhiteSpace(current))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var detachedConnectionIds = GetConnectionIdsForUserInSession(userId, sessionId);

            if (remainingParticipants.Length == 0)
            {
                var historyRecords = dbContext.SessionRollHistoryRecords
                    .Where(current => current.SessionId == sessionId);

                dbContext.SessionRollHistoryRecords.RemoveRange(historyRecords);
                dbContext.SessionParticipantRecords.RemoveRange(record.Participants);
                dbContext.SessionRecords.Remove(record);
                dbContext.SaveChanges();

                _sessions.TryRemove(sessionId, out _);
                _codeMap.TryRemove(session.JoinCode, out _);
                RemoveActiveSessionMappingsForUserInSession(userId, sessionId);

                return new LeaveSessionResult(true, affectedUserIds, detachedConnectionIds);
            }

            if (string.Equals(record.MasterUserId, userId, StringComparison.Ordinal))
            {
                foreach (var current in record.Participants)
                {
                    current.IsMaster = false;
                }

                var nextMaster = remainingParticipants[0];
                nextMaster.IsMaster = true;
                record.MasterUserId = nextMaster.UserId;
            }

            dbContext.SessionParticipantRecords.Remove(participant);
            dbContext.SaveChanges();

            session.MasterUserId = record.MasterUserId;
            ReplaceMemoryPlayers(session, remainingParticipants);
            RemoveActiveSessionMappingsForUserInSession(userId, sessionId);

            return new LeaveSessionResult(false, affectedUserIds, detachedConnectionIds);
        }
    }

    public GameSession RenameSession(string sessionId, string userId, string? sessionName)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = GetRequiredSession(sessionId);
            EnsureMaster(session, userId);

            var resolvedName = ResolveSessionName(sessionName);

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
            var record = dbContext.SessionRecords.SingleOrDefault(current => current.Id == sessionId) ??
                         throw new InvalidOperationException("Session wurde nicht gefunden.");

            record.Name = resolvedName;
            dbContext.SaveChanges();

            session.Name = resolvedName;
            return session;
        }
    }

    public GameSession RenamePlayer(string sessionId, string userId, string? playerName)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = GetRequiredSession(sessionId);
            EnsureMember(session, userId);

            var resolvedName = ResolveRequiredPlayerName(playerName);

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
            var record = dbContext.SessionRecords
                             .Include(current => current.Participants)
                             .SingleOrDefault(current => current.Id == sessionId) ??
                         throw new InvalidOperationException("Session wurde nicht gefunden.");

            var participant = record.Participants
                                  .FirstOrDefault(current =>
                                      string.Equals(current.UserId, userId, StringComparison.Ordinal)) ??
                              throw new InvalidOperationException("Spieler ist nicht Teil dieser Session.");

            participant.Name = resolvedName;
            dbContext.SaveChanges();

            var player = session.Players.FirstOrDefault(current =>
                string.Equals(current.UserId, userId, StringComparison.Ordinal));

            if (player is not null)
            {
                player.Name = resolvedName;
            }

            return session;
        }
    }

    public IReadOnlyList<string> DeleteSession(string sessionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = GetRequiredSession(sessionId);
            EnsureMaster(session, userId);

            var affectedUserIds = session.Players
                .Select(player => player.UserId)
                .Where(currentUserId => !string.IsNullOrWhiteSpace(currentUserId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
            var record = dbContext.SessionRecords
                             .Include(current => current.Participants)
                             .SingleOrDefault(current => current.Id == sessionId) ??
                         throw new InvalidOperationException("Session wurde nicht gefunden.");

            var historyRecords = dbContext.SessionRollHistoryRecords
                .Where(current => current.SessionId == sessionId);

            dbContext.SessionRollHistoryRecords.RemoveRange(historyRecords);
            dbContext.SessionParticipantRecords.RemoveRange(record.Participants);
            dbContext.SessionRecords.Remove(record);
            dbContext.SaveChanges();

            _sessions.TryRemove(sessionId, out _);
            _codeMap.TryRemove(session.JoinCode, out _);
            RemoveActiveSessionMappingsForSession(sessionId);

            return affectedUserIds;
        }
    }

    public IReadOnlyList<SessionSummaryDto> GetSessionsForUser(string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            return _sessions.Values
                .Where(session =>
                    session.Players.Any(player => string.Equals(player.UserId, userId, StringComparison.Ordinal)))
                .OrderBy(session => session.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(session => session.CreatedAt)
                .Select(ToSummary)
                .ToArray();
        }
    }

    public IReadOnlyList<string> RegisterConnection(string userId, string connectionId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

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
    }

    public IReadOnlyList<string> UnregisterConnection(string connectionId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

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
    }

    public string ResolvePlayerName(string sessionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = GetRequiredSession(sessionId);
            EnsureMember(session, userId);

            return session.Players.FirstOrDefault(player =>
                string.Equals(player.UserId, userId, StringComparison.Ordinal))?.Name ?? "Jemand";
        }
    }

    public GameSession RequireRollSession(string? sessionId, string connectionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException("Fuer diesen Wurf ist keine aktive Session ausgewaehlt.");
            }

            var session = GetRequiredSession(sessionId);
            EnsureMember(session, userId);

            if (!_activeSessionByConnection.TryGetValue(connectionId, out var activeSessionId) ||
                !string.Equals(activeSessionId, sessionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Diese Session ist aktuell nicht geoeffnet.");
            }

            return session;
        }
    }

    public void AppendHistoryEntry(string sessionId, RollHistoryEntryDto historyEntry)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();

            dbContext.SessionRollHistoryRecords.Add(new SessionRollHistoryRecord
            {
                SessionId = sessionId,
                PlayerName = historyEntry.PlayerName,
                TimestampUtc = historyEntry.Timestamp,
                RollsJson = JsonSerializer.Serialize(historyEntry.Rolls, JsonOptions),
                Modifier = historyEntry.Modifier,
                TotalSum = historyEntry.TotalSum
            });
            dbContext.SaveChanges();

            var staleHistory = dbContext.SessionRollHistoryRecords
                .Where(current => current.SessionId == sessionId)
                .OrderByDescending(current => current.TimestampUtc)
                .ThenByDescending(current => current.Id)
                .Skip(MaxPersistedHistoryEntries)
                .ToArray();

            if (staleHistory.Length > 0)
            {
                dbContext.SessionRollHistoryRecords.RemoveRange(staleHistory);
                dbContext.SaveChanges();
            }

            session.History.Add(historyEntry);
        }
    }

    private void EnsureLoaded()
    {
        if (_initialized)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
        var records = dbContext.SessionRecords
            .AsNoTracking()
            .Include(session => session.Participants)
            .OrderBy(session => session.CreatedAtUtc)
            .ToArray();

        _sessions.Clear();
        _codeMap.Clear();

        foreach (var record in records)
        {
            var session = ToGameSession(record);
            _sessions[session.SessionId] = session;
            _codeMap[session.JoinCode] = session.SessionId;
        }

        _initialized = true;
    }

    private IReadOnlyList<string> GetPresenceAffectedUserIds(string userId)
    {
        return _sessions.Values
            .Where(session =>
                session.Players.Any(player => string.Equals(player.UserId, userId, StringComparison.Ordinal)))
            .SelectMany(session => session.Players.Select(player => player.UserId))
            .Where(current => !string.IsNullOrWhiteSpace(current))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private RollHistoryEntryDto[] LoadPersistedHistory(string sessionId)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();

        return dbContext.SessionRollHistoryRecords
            .AsNoTracking()
            .Where(current => current.SessionId == sessionId)
            .OrderByDescending(current => current.TimestampUtc)
            .ThenByDescending(current => current.Id)
            .Take(MaxPersistedHistoryEntries)
            .Select(ToHistoryEntry)
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
                IsUserOnline(player.UserId)))
            .ToArray();
    }

    private bool IsUserOnline(string userId)
    {
        return !string.IsNullOrWhiteSpace(userId) &&
               _connectionsByUser.TryGetValue(userId, out var connections) &&
               connections.Count > 0;
    }

    private static void UpsertMemoryPlayer(GameSession session, PlayerInfo player)
    {
        var existing = session.Players.FirstOrDefault(current =>
            string.Equals(current.UserId, player.UserId, StringComparison.Ordinal));

        if (existing is null)
        {
            session.Players.Add(player);
            return;
        }

        existing.ConnectionId = player.ConnectionId;
        existing.Name = player.Name;
        existing.AvatarUrl = player.AvatarUrl;
        existing.IsMaster = existing.IsMaster || player.IsMaster;
    }

    private void ReplaceMemoryPlayers(GameSession session, IEnumerable<SessionParticipantRecord> participants)
    {
        var existingByUserId = session.Players
            .GroupBy(player => player.UserId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        session.Players = new ConcurrentBag<PlayerInfo>(participants.Select(participant => new PlayerInfo
        {
            UserId = participant.UserId,
            Name = participant.Name,
            AvatarUrl = participant.AvatarUrl,
            IsMaster = participant.IsMaster,
            ConnectionId = existingByUserId.GetValueOrDefault(participant.UserId)?.ConnectionId ?? string.Empty
        }));
    }

    private static GameSession ToGameSession(SessionRecord record)
    {
        return new GameSession
        {
            SessionId = record.Id,
            Name = record.Name,
            JoinCode = record.JoinCode,
            MasterUserId = record.MasterUserId,
            CreatedAt = record.CreatedAtUtc,
            Players = new ConcurrentBag<PlayerInfo>(record.Participants.Select(participant => new PlayerInfo
            {
                UserId = participant.UserId,
                Name = participant.Name,
                AvatarUrl = participant.AvatarUrl,
                IsMaster = participant.IsMaster
            }))
        };
    }

    private static SessionParticipantRecord ToParticipantRecord(string sessionId, PlayerInfo player)
    {
        return new SessionParticipantRecord
        {
            SessionId = sessionId,
            UserId = player.UserId,
            Name = player.Name,
            AvatarUrl = player.AvatarUrl,
            IsMaster = player.IsMaster
        };
    }

    private static RollHistoryEntryDto ToHistoryEntry(SessionRollHistoryRecord record)
    {
        return new RollHistoryEntryDto(
            record.PlayerName,
            record.TimestampUtc,
            JsonSerializer.Deserialize<DiceRollDto[]>(record.RollsJson, JsonOptions) ?? Array.Empty<DiceRollDto>(),
            record.Modifier,
            record.TotalSum);
    }

    private static string ResolveSessionName(string? sessionName)
    {
        return string.IsNullOrWhiteSpace(sessionName)
            ? "Unbenannte Session"
            : sessionName.Trim();
    }

    private static string ResolveRequiredPlayerName(string? playerName)
    {
        return string.IsNullOrWhiteSpace(playerName)
            ? throw new InvalidOperationException("Bitte einen Spielernamen eingeben.")
            : playerName.Trim();
    }

    private static string NormalizeJoinCode(string code)
    {
        return code.Trim().ToUpperInvariant();
    }

    private GameSession GetRequiredSession(string sessionId)
    {
        return _sessions.GetValueOrDefault(sessionId) ??
               throw new InvalidOperationException("Session wurde nicht gefunden.");
    }

    private static void EnsureMaster(GameSession session, string userId)
    {
        if (!string.Equals(session.MasterUserId, userId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Nur der Meister kann diese Session verwalten.");
        }
    }

    private static void EnsureMember(GameSession session, string userId)
    {
        if (!session.Players.Any(player => string.Equals(player.UserId, userId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Spieler ist nicht Teil dieser Session.");
        }
    }

    private void RemoveActiveSessionMappingsForSession(string sessionId)
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

    private void RemoveActiveSessionMappingsForUserInSession(string userId, string sessionId)
    {
        var connectionIds = GetConnectionIdsForUserInSession(userId, sessionId);

        foreach (var connectionId in connectionIds)
        {
            _activeSessionByConnection.Remove(connectionId);
        }
    }

    private string[] GetConnectionIdsForUserInSession(string userId, string sessionId)
    {
        return _activeSessionByConnection
            .Where(current =>
                string.Equals(current.Value, sessionId, StringComparison.Ordinal) &&
                string.Equals(_userByConnection.GetValueOrDefault(current.Key), userId, StringComparison.Ordinal))
            .Select(current => current.Key)
            .ToArray();
    }

    private string GenerateUniqueJoinCode(HeroDbContext dbContext)
    {
        const int maxAttempts = 64;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var joinCode = GenerateJoinCode();
            if (_codeMap.ContainsKey(joinCode))
            {
                continue;
            }

            var existsInStore = dbContext.SessionRecords.Any(session => session.JoinCode == joinCode);
            if (!existsInStore)
            {
                return joinCode;
            }
        }

        throw new InvalidOperationException("Es konnte kein eindeutiger Session-Code erzeugt werden.");
    }

    private static string GenerateJoinCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable
            .Repeat(chars, 4)
            .Select(current => current[Random.Shared.Next(current.Length)])
            .ToArray());
    }
}

public sealed record LeaveSessionResult(
    bool SessionDeleted,
    IReadOnlyList<string> AffectedUserIds,
    IReadOnlyList<string> DetachedConnectionIds);