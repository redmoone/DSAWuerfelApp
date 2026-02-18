using System.Security.Cryptography;

namespace DsaWuerfelApp.Services;

public class DiceService
{
    public RollSetResult RollSet(IReadOnlyList<DiceGroup> dice, int modifier)
    {
        if (dice is null || dice.Count == 0)
            throw new ArgumentException("No dice selected.", nameof(dice));

        if (modifier < -999 || modifier > 999)
            throw new ArgumentOutOfRangeException(nameof(modifier));

        var allRolls = new List<SingleRoll>(capacity: dice.Sum(d => d.Count));

        foreach (var g in dice)
        {
            if (g.Count < 1 || g.Count > 100) throw new ArgumentOutOfRangeException(nameof(g.Count));
            if (g.Sides < 2) throw new ArgumentOutOfRangeException(nameof(g.Sides));

            for (int i = 0; i < g.Count; i++)
            {
                int r = RandomNumberGenerator.GetInt32(1, g.Sides + 1);
                allRolls.Add(new SingleRoll(g.Sides, r));
            }
        }

        var sum = allRolls.Sum(r => r.Value);
        return new RollSetResult(dice.ToArray(), modifier, allRolls.ToArray(), sum, sum + modifier);
    }
}

public record DiceGroup(int Sides, int Count);
public record SingleRoll(int Sides, int Value);
public record RollSetResult(DiceGroup[] Dice, int Modifier, SingleRoll[] Rolls, int Sum, int Total);