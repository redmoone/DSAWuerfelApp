namespace DsaWuerfelApp.Shared;

public sealed record SessionPlayerDto(
    string UserId,
    string Name,
    bool IsMaster,
    bool IsOnline);

public sealed record SessionSummaryDto(
    string SessionId,
    string Name,
    string JoinCode,
    string MasterUserId,
    SessionPlayerDto[] Players);

public sealed record SessionDetailsDto(
    string SessionId,
    string Name,
    string JoinCode,
    string MasterUserId,
    SessionPlayerDto[] Players,
    RollHistoryEntryDto[] History);