namespace DsaWuerfelApp.Shared;

public enum SchlechteEigenschaftProbeStatus
{
    Bestanden,
    Misslungen
}

public class SchlechteEigenschaftProbeRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string EigenschaftName { get; set; } = string.Empty;
    public int EigenschaftWert { get; set; }
    public int? ForcedRoll { get; set; }
}

public class SchlechteEigenschaftProbeResult
{
    public string PlayerName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string EigenschaftName { get; set; } = string.Empty;
    public int EigenschaftWert { get; set; }
    public int TargetValue { get; set; }
    public SingleRoll Roll { get; set; } = new() { Sides = 20 };
    public SchlechteEigenschaftProbeStatus Status { get; set; }
    public bool Success { get; set; }
    public bool EigenschaftSetztSichDurch { get; set; }
    public int Margin { get; set; }
}