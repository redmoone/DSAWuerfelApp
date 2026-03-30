namespace DsaWuerfelApp.Shared;

public enum TalentProbeStatus
{
    Bestanden,
    NichtBestanden,
    Patzer,
    GluecklicherWurf
}

public class TalentProbeRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string TalentName { get; set; } = string.Empty;
    public int TalentValue { get; set; }
    public string Probe { get; set; } = string.Empty;
    public Dictionary<string, int> AttributeValues { get; set; } = new();
    public List<int>? ForcedRolls { get; set; }
    public int Modifier { get; set; }
}

public class TalentProbeResult
{
    public string PlayerName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string TalentName { get; set; } = string.Empty;
    public int TalentValue { get; set; }
    public string Probe { get; set; } = string.Empty;
    public int Modifier { get; set; }
    public int EffectiveTalentValue { get; set; }
    public List<SingleRoll> Rolls { get; set; } = [];
    public List<TalentProbeRollDetail> Details { get; set; } = [];
    public TalentProbeStatus Status { get; set; }
    public int Rest { get; set; }
    public bool Success { get; set; }
    public int Margin { get; set; }
}

public class TalentProbeRollDetail
{
    public string Attribute { get; set; } = string.Empty;
    public int BaseValue { get; set; }
    public int TargetValue { get; set; }
    public int Roll { get; set; }
    public int Difference { get; set; }
    public int RemainingRest { get; set; }
    public bool Success { get; set; }
}