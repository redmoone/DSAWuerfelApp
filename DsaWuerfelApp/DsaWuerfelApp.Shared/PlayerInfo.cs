namespace DsaWuerfelApp.Shared;

public class PlayerInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = "Unbekannt";
    public string AvatarUrl { get; set; } = string.Empty;
    public bool IsMaster { get; set; }
}