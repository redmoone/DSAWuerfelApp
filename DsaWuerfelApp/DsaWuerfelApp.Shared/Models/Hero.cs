using System.Text.Json.Serialization;

namespace DsaWuerfelApp.Shared.Models;

public class Hero
{
    public Guid Id { get; set; } = Guid.Empty;
    public bool IsActive { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Geschlecht { get; set; } = string.Empty;
    public int Alter { get; set; }
    public Dictionary<string, int> Eigenschaften { get; set; } = new();
    public Dictionary<string, int> SchlechteEigenschaften { get; set; } = new();
    public Dictionary<string, TalentData> Talente { get; set; } = new();
    public Dictionary<string, TalentData> Zauber { get; set; } = new();
    [JsonIgnore] public byte[]? SourceXml { get; set; }
    [JsonIgnore] public string? SourceFileName { get; set; }
    [JsonIgnore] public int ImportVersion { get; set; }
    [JsonIgnore] public DateTime? ImportedAtUtc { get; set; }
}

public sealed class TalentData
{
    public int Wert { get; set; }
    public string Probe { get; set; } = string.Empty;
    public string[] Specializations { get; set; } = [];
}