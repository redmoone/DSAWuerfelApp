namespace DsaWuerfelApp.Shared;

public class RollRequest
{
    public string SessionId { get; set; } = string.Empty;
    public List<DiceGroup> Dice { get; set; } = [];
    public int Modifier { get; set; }
}

public class DiceGroup
{
    public DiceGroup() { }

    public DiceGroup(int sides, int count)
    {
        Sides = sides;
        Count = count;
    }

    public int Count { get; set; }

    public int Sides { get; set; } = 20;

    public string? Color { get; set; }

    public string DiceType
    {
        get => $"d{Sides}";
        set
        {
            if (!string.IsNullOrEmpty(value) && value.StartsWith("d") && int.TryParse(value.Substring(1), out int s))
            {
                Sides = s;
            }
        }
    }
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