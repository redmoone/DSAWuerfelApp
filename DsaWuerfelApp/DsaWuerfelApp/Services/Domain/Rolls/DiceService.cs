using System.Security.Cryptography;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public class DiceService
{
    public DiceRollDto[] RollDice(IReadOnlyList<DiceRollGroupDto> dice)
    {
        if (dice is null || dice.Count == 0)
        {
            throw new ArgumentException("No dice selected.", nameof(dice));
        }

        var allRolls = new List<DiceRollDto>(capacity: dice.Sum(group => group.Count));

        foreach (var group in dice)
        {
            ValidateGroup(group);

            for (var index = 0; index < group.Count; index++)
            {
                allRolls.Add(new DiceRollDto(group.Sides, RandomNumberGenerator.GetInt32(1, group.Sides + 1)));
            }
        }

        return allRolls.ToArray();
    }

    public RollResult RollSet(IReadOnlyList<DiceGroup> dice, int modifier, string playerName = "Unbekannt")
    {
        if (dice is null)
        {
            throw new ArgumentNullException(nameof(dice));
        }

        ValidateModifier(modifier);

        var rolls = RollDice(dice.Select(group => new DiceRollGroupDto(group.Sides, group.Count)).ToArray());
        var sum = rolls.Sum(roll => roll.Value);

        return new RollResult
        {
            PlayerName = playerName,
            Timestamp = DateTime.UtcNow,
            Rolls = rolls.Select(roll => new SingleRoll { Sides = roll.Sides, Value = roll.Value }).ToList(),
            Modifier = modifier,
            TotalSum = sum + modifier
        };
    }

    internal static void ValidateModifier(int modifier)
    {
        if (modifier is < -999 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(modifier));
        }
    }

    private static void ValidateGroup(DiceRollGroupDto group)
    {
        if (group.Count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(group.Count));
        }

        if (group.Sides < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(group.Sides));
        }
    }
}