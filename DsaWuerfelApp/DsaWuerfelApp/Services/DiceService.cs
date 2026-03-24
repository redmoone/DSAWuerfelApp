using System.Security.Cryptography;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public class DiceService
{
    public RollResult RollSet(IReadOnlyList<DiceGroup> dice, int modifier, string playerName = "Unbekannt")
    {
        if (dice is null || dice.Count == 0)
            throw new ArgumentException("No dice selected.", nameof(dice));

        if (modifier is < -999 or > 999)
            throw new ArgumentOutOfRangeException(nameof(modifier));

        var allRolls = new List<SingleRoll>(capacity: dice.Sum(d => d.Count));

        foreach (var g in dice)
        {
            ArgumentOutOfRangeException
                exception = new(nameof(g.Count)) { HelpLink = null, HResult = 0, Source = null };
            if (g.Count is < 1 or > 100)
            {
                throw exception;
            }

            if (g.Sides < 2) throw new ArgumentOutOfRangeException(nameof(g.Sides));

            for (int i = 0; i < g.Count; i++)
            {
                int r = RandomNumberGenerator.GetInt32(1, g.Sides + 1);
                allRolls.Add(new SingleRoll { Sides = g.Sides, Value = r });
            }
        }

        var sum = allRolls.Sum(r => r.Value);

        return new RollResult
        {
            PlayerName = playerName,
            Timestamp = DateTime.UtcNow,
            Rolls = allRolls,
            Modifier = modifier,
            TotalSum = sum + modifier
        };
    }
}