namespace DsaWuerfelApp.Shared.Models;

public class Hero
{
    public Guid Id { get; set; } = Guid.Empty;
    public bool IsActive { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Geschlecht { get; set; } = string.Empty;
    public int Alter { get; set; }
    public Dictionary<string, int> Eigenschaften { get; set; } = new();
    public Dictionary<string, TalentData> Talente { get; set; } = new();
}

public sealed class TalentData
{
    public int Wert { get; set; }
    public string Probe { get; set; } = string.Empty;
}