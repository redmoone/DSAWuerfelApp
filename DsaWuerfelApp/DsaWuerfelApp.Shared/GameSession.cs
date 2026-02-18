namespace DsaWuerfelApp.Shared;

public class GameSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string JoinCode { get; set; } = string.Empty;
    public string MasterUserId { get; set; } = string.Empty; 
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public List<PlayerInfo> Players { get; set; } = [];
    public List<RollResult> History { get; set; } = [];
}