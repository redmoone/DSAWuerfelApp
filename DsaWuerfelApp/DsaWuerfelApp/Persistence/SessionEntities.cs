namespace DsaWuerfelApp.Persistence;

public sealed class SessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string JoinCode { get; set; } = string.Empty;
    public string MasterUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<SessionParticipantRecord> Participants { get; set; } = [];
}

public sealed class SessionParticipantRecord
{
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = "Unbekannt";
    public string AvatarUrl { get; set; } = string.Empty;
    public bool IsMaster { get; set; }

    public SessionRecord? Session { get; set; }
}

public sealed class SessionRollHistoryRecord
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = "Unbekannt";
    public DateTime TimestampUtc { get; set; }
    public string RollsJson { get; set; } = "[]";
    public int Modifier { get; set; }
    public int TotalSum { get; set; }
}