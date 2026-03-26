namespace DsaWuerfelApp.Shared.Models;

public class Hero
{
    public Guid Id { get; set; } = Guid.Empty;
    public string Name { get; set; } = string.Empty;
    public string Geschlecht { get; set; } = string.Empty;
    public int Alter { get; set; }
    public Dictionary<string, int> Eigenschaften { get; set; } = new();
    public Dictionary<string, int> Talente { get; set; } = new();
}