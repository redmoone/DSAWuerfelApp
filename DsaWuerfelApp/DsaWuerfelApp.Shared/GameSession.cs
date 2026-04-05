namespace DsaWuerfelApp.Shared;

public class GameSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string JoinCode { get; set; } = string.Empty;
    public string MasterUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public PlayerInfo[] Players { get; set; } = [];
}