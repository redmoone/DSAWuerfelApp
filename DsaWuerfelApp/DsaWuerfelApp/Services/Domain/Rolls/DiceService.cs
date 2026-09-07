using System.Security.Cryptography;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public class DiceService
{
    public const int MaxDicePerGroup = 100;
    public const int MaxTotalDice = 100;
    public const int MinSides = 2;
    public const int MaxSides = 1_000_000;

    public DiceRollDto[] RollDice(IReadOnlyList<DiceRollGroupDto> dice)
    {
        if (dice is null || dice.Count == 0)
        {
            throw new ArgumentException("No dice selected.", nameof(dice));
        }

        var totalDice = 0;
        foreach (var group in dice)
        {
            ValidateGroup(group);
            totalDice = checked(totalDice + group.Count);
        }

        if (totalDice > MaxTotalDice)
        {
            throw new ArgumentOutOfRangeException(nameof(dice), $"Maximal {MaxTotalDice} Würfel sind erlaubt.");
        }

        var allRolls = new List<DiceRollDto>(capacity: totalDice);

        foreach (var group in dice)
        {
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
        var sum = checked(rolls.Sum(roll => roll.Value));

        return new RollResult
        {
            PlayerName = playerName,
            Timestamp = DateTime.UtcNow,
            Rolls = rolls.Select(roll => new SingleRoll { Sides = roll.Sides, Value = roll.Value }).ToList(),
            Modifier = modifier,
            TotalSum = checked(sum + modifier)
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
        if (group is null)
        {
            throw new ArgumentException("Dice groups may not be null.", nameof(group));
        }

        if (group.Count is < 1 or > MaxDicePerGroup)
        {
            throw new ArgumentOutOfRangeException(nameof(group.Count));
        }

        if (group.Sides is < MinSides or > MaxSides)
        {
            throw new ArgumentOutOfRangeException(nameof(group.Sides));
        }
    }
}
