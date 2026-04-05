using System.Text.Json;

using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared;

using Microsoft.EntityFrameworkCore;

namespace DsaWuerfelApp.Services;

public sealed class SessionRecordStore(IServiceScopeFactory scopeFactory)
{
    private const int MaxPersistedHistoryEntries = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public GameSession[] LoadSessions()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();

        return dbContext.SessionRecords
            .AsNoTracking()
            .Include(session => session.Participants)
            .OrderBy(session => session.CreatedAtUtc)
            .Select(ToGameSession)
            .ToArray();
    }

    public GameSession CreateSession(string masterUserId, string masterName, string sessionName, string joinCode)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();

        var record = new SessionRecord
        {
            Id = Guid.NewGuid().ToString(),
            MasterUserId = masterUserId,
            JoinCode = joinCode,
            Name = sessionName,
            CreatedAtUtc = DateTime.UtcNow,
            Participants =
            [
                new SessionParticipantRecord { UserId = masterUserId, Name = masterName, IsMaster = true }
            ]
        };

        dbContext.SessionRecords.Add(record);
        dbContext.SaveChanges();

        return ToGameSession(record);
    }

    public bool UpsertPlayer(string sessionId, PlayerInfo player)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
        var record = FindSessionRecordWithParticipants(dbContext, sessionId);
        if (record is null)
        {
            return false;
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
            participant.IsMaster |= player.IsMaster;
        }

        dbContext.SaveChanges();
        return true;
    }

    public RollHistoryEntryDto[] LoadHistory(string sessionId)
    {
        using var scope = scopeFactory.CreateScope();
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

    public PersistedLeaveSessionResult LeaveSession(string sessionId, string userId)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
        var record = GetRequiredSessionRecordWithParticipants(dbContext, sessionId);
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

        if (remainingParticipants.Length == 0)
        {
            DeleteSession(dbContext, record);
            return new PersistedLeaveSessionResult(true, string.Empty, [], []);
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

        return new PersistedLeaveSessionResult(
            false,
            record.MasterUserId,
            remainingParticipants.Select(ToPlayerInfo).ToArray(),
            remainingParticipants.Select(current => current.UserId).ToArray());
    }

    public void RenameSession(string sessionId, string sessionName)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
        var record = dbContext.SessionRecords.SingleOrDefault(current => current.Id == sessionId) ??
                     throw new InvalidOperationException("Session wurde nicht gefunden.");

        record.Name = sessionName;
        dbContext.SaveChanges();
    }

    public void RenamePlayer(string sessionId, string userId, string playerName)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
        var record = GetRequiredSessionRecordWithParticipants(dbContext, sessionId);
        var participant = record.Participants
                              .FirstOrDefault(current =>
                                  string.Equals(current.UserId, userId, StringComparison.Ordinal)) ??
                          throw new InvalidOperationException("Spieler ist nicht Teil dieser Session.");

        participant.Name = playerName;
        dbContext.SaveChanges();
    }

    public void DeleteSession(string sessionId)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
        var record = GetRequiredSessionRecordWithParticipants(dbContext, sessionId);

        DeleteSession(dbContext, record);
    }

    public void AppendHistoryEntry(string sessionId, RollHistoryEntryDto historyEntry)
    {
        using var scope = scopeFactory.CreateScope();
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

        if (staleHistory.Length == 0)
        {
            return;
        }

        dbContext.SessionRollHistoryRecords.RemoveRange(staleHistory);
        dbContext.SaveChanges();
    }

    public bool JoinCodeExists(string joinCode)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
        return dbContext.SessionRecords.Any(session => session.JoinCode == joinCode);
    }

    private static SessionRecord? FindSessionRecordWithParticipants(HeroDbContext dbContext, string sessionId)
    {
        return dbContext.SessionRecords
            .Include(current => current.Participants)
            .SingleOrDefault(current => current.Id == sessionId);
    }

    private static SessionRecord GetRequiredSessionRecordWithParticipants(HeroDbContext dbContext, string sessionId)
    {
        return FindSessionRecordWithParticipants(dbContext, sessionId) ??
               throw new InvalidOperationException("Session wurde nicht gefunden.");
    }

    private static void DeleteSession(HeroDbContext dbContext, SessionRecord record)
    {
        var historyRecords = dbContext.SessionRollHistoryRecords
            .Where(current => current.SessionId == record.Id);

        dbContext.SessionRollHistoryRecords.RemoveRange(historyRecords);
        dbContext.SessionParticipantRecords.RemoveRange(record.Participants);
        dbContext.SessionRecords.Remove(record);
        dbContext.SaveChanges();
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
            Players = record.Participants
                .Select(ToPlayerInfo)
                .ToArray()
        };
    }

    private static PlayerInfo ToPlayerInfo(SessionParticipantRecord participant)
    {
        return new PlayerInfo
        {
            UserId = participant.UserId,
            Name = participant.Name,
            AvatarUrl = participant.AvatarUrl,
            IsMaster = participant.IsMaster
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
}

public sealed record PersistedLeaveSessionResult(
    bool SessionDeleted,
    string MasterUserId,
    PlayerInfo[] RemainingPlayers,
    IReadOnlyList<string> AffectedUserIds);