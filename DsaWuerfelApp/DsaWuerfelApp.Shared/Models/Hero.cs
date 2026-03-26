namespace DsaWuerfelApp.Shared.Models;

public class Hero
{
    public Guid Id { get; init; } = Guid.Empty;
    public string Name { get; init; } = string.Empty;
    public string Geschlecht { get; init; } = string.Empty;
    public int Alter { get; init; }
    public Dictionary<string, int> Eigenschaften { get; init; } = new();
    public Dictionary<string, int> Talente { get; init; } = new();
}