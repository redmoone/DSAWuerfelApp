using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

internal static class DiceResultFactory
{
    public static RollEquationDto CreateEquation(IReadOnlyList<DiceRollDto> rolls, int modifier)
    {
        var rollArray = rolls.ToArray();
        var sum = rollArray.Sum(roll => roll.Value);

        return new RollEquationDto(
            rollArray
                .GroupBy(roll => roll.Sides)
                .Select(group => new DiceRollGroupDto(group.Key, group.Count()))
                .ToArray(),
            modifier,
            rollArray,
            sum,
            sum + modifier);
    }

    public static RollHistoryEntryDto CreateHistoryEntry(string playerName, DateTime timestamp,
        RollEquationDto equation)
    {
        return new RollHistoryEntryDto(playerName, timestamp, equation.Rolls, equation.Modifier, equation.Total);
    }
}