namespace DsaWuerfelApp.Shared;

public class RollRequest
{
    public string SessionId { get; set; } = string.Empty;
    public List<DiceGroup> Dice { get; set; } = [];
    public int Modifier { get; set; }
}

public class DiceGroup
{
    public int Sides { get; set; }
    public int Count { get; set; }
    
    public DiceGroup() {}
    public DiceGroup(int sides, int count) { Sides = sides; Count = count; }
}

public class RollResult
{
    public string PlayerName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<SingleRoll> Rolls { get; set; } = [];
    public int Modifier { get; set; }
    public int TotalSum { get; set; }
}

public class SingleRoll
{
    public int Sides { get; set; }
    public int Value { get; set; }
}