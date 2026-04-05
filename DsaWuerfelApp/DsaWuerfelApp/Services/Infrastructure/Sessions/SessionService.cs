using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public class SessionService(SessionRecordStore recordStore, SessionRuntimeState runtimeState)
{
    private readonly object _syncRoot = new();

    public GameSession CreateSession(string masterUserId, string masterName, string? sessionName)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = recordStore.CreateSession(
                masterUserId,
                masterName,
                ResolveSessionName(sessionName),
                GenerateUniqueJoinCode());

            runtimeState.AddSession(session);
            return session;
        }
    }

    public GameSession? GetByCode(string code)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();
            return runtimeState.GetByCode(code);
        }
    }

    public GameSession OpenSession(string sessionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();
            return runtimeState.GetMemberSession(sessionId, userId);
        }
    }

    public void AddPlayer(string sessionId, PlayerInfo player)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            if (!runtimeState.TryGetSession(sessionId, out var session))
            {
                return;
            }

            if (!recordStore.UpsertPlayer(sessionId, player))
            {
                return;
            }

            runtimeState.UpsertPlayer(session, player);
        }
    }

    public string? ActivateSessionConnection(string sessionId, string userId, string connectionId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();
            return runtimeState.ActivateSessionConnection(sessionId, userId, connectionId);
        }
    }

    public SessionDetailsDto GetSessionDetails(string sessionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = runtimeState.GetMemberSession(sessionId, userId);
            return runtimeState.BuildSessionDetails(session, recordStore.LoadHistory(sessionId));
        }
    }

    public LeaveSessionResult LeaveSession(string sessionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            runtimeState.GetMemberSession(sessionId, userId);
            var detachedConnectionIds = runtimeState.GetConnectionIdsForUserInSession(userId, sessionId);
            var persistedResult = recordStore.LeaveSession(sessionId, userId);

            if (persistedResult.SessionDeleted)
            {
                runtimeState.RemoveSession(sessionId);
                runtimeState.RemoveActiveSessionMappingsForUserInSession(userId, sessionId);

                return new LeaveSessionResult(
                    true,
                    GetAffectedUserIds(persistedResult.AffectedUserIds.Append(userId)),
                    detachedConnectionIds);
            }

            var session = runtimeState.GetRequiredSession(sessionId);
            runtimeState.ReplacePlayers(session, persistedResult.MasterUserId, persistedResult.RemainingPlayers);
            runtimeState.RemoveActiveSessionMappingsForUserInSession(userId, sessionId);

            return new LeaveSessionResult(
                false,
                GetAffectedUserIds(persistedResult.AffectedUserIds.Append(userId)),
                detachedConnectionIds);
        }
    }

    public GameSession RenameSession(string sessionId, string userId, string? sessionName)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = runtimeState.GetRequiredSession(sessionId);
            EnsureMaster(session, userId);

            var resolvedName = ResolveSessionName(sessionName);
            recordStore.RenameSession(sessionId, resolvedName);
            session.Name = resolvedName;

            return session;
        }
    }

    public GameSession RenamePlayer(string sessionId, string userId, string? playerName)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = runtimeState.GetMemberSession(sessionId, userId);
            var resolvedName = ResolveRequiredPlayerName(playerName);

            recordStore.RenamePlayer(sessionId, userId, resolvedName);
            runtimeState.RenamePlayer(session, userId, resolvedName);

            return session;
        }
    }

    public IReadOnlyList<string> DeleteSession(string sessionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            var session = runtimeState.GetRequiredSession(sessionId);
            EnsureMaster(session, userId);

            var affectedUserIds = runtimeState.GetAffectedUserIds(session);
            recordStore.DeleteSession(sessionId);
            runtimeState.RemoveSession(sessionId);
            runtimeState.RemoveActiveSessionMappingsForSession(sessionId);

            return affectedUserIds;
        }
    }

    public IReadOnlyList<SessionSummaryDto> GetSessionsForUser(string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();
            return runtimeState.GetSessionsForUser(userId);
        }
    }

    public IReadOnlyList<string> RegisterConnection(string userId, string connectionId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();
            return runtimeState.RegisterConnection(userId, connectionId);
        }
    }

    public IReadOnlyList<string> UnregisterConnection(string connectionId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();
            return runtimeState.UnregisterConnection(connectionId);
        }
    }

    public string ResolvePlayerName(string sessionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();
            return runtimeState.ResolvePlayerName(sessionId, userId);
        }
    }

    public GameSession RequireRollSession(string? sessionId, string connectionId, string userId)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();
            return runtimeState.RequireRollSession(sessionId, connectionId, userId);
        }
    }

    public void AppendHistoryEntry(string sessionId, RollHistoryEntryDto historyEntry)
    {
        lock (_syncRoot)
        {
            EnsureLoaded();

            if (!runtimeState.ContainsSession(sessionId))
            {
                return;
            }

            recordStore.AppendHistoryEntry(sessionId, historyEntry);
        }
    }

    private void EnsureLoaded()
    {
        if (runtimeState.IsInitialized)
        {
            return;
        }

        runtimeState.Initialize(recordStore.LoadSessions());
    }

    private string GenerateUniqueJoinCode()
    {
        const int maxAttempts = 64;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var joinCode = GenerateJoinCode();
            if (runtimeState.ContainsJoinCode(joinCode) || recordStore.JoinCodeExists(joinCode))
            {
                continue;
            }

            return joinCode;
        }

        throw new InvalidOperationException("Es konnte kein eindeutiger Session-Code erzeugt werden.");
    }

    private static void EnsureMaster(GameSession session, string userId)
    {
        if (!string.Equals(session.MasterUserId, userId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Nur der Meister kann diese Session verwalten.");
        }
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

    private static IReadOnlyList<string> GetAffectedUserIds(IEnumerable<string> userIds)
    {
        return userIds
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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