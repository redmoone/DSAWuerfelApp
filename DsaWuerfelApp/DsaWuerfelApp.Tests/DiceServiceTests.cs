using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Tests;

public sealed class DiceServiceTests
{
    private readonly DiceService _service = new();

    [Fact]
    public void Null_and_empty_requests_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => _service.RollDice(null!));
        Assert.Throws<ArgumentException>(() => _service.RollDice([]));
    }

    [Fact]
    public void Null_group_is_rejected()
    {
        IReadOnlyList<DiceRollGroupDto> dice = new DiceRollGroupDto?[] { null! }!;

        Assert.Throws<ArgumentException>(() => _service.RollDice(dice));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(101, 20)]
    [InlineData(int.MaxValue, 20)]
    public void Invalid_counts_are_rejected(int count, int sides)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.RollDice([new DiceRollGroupDto(sides, count)]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_000_001)]
    [InlineData(int.MaxValue)]
    public void Invalid_side_counts_are_rejected(int sides)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.RollDice([new DiceRollGroupDto(sides, 1)]));
    }

    [Fact]
    public void Total_count_is_limited_before_rolls_are_created()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.RollDice([
                new DiceRollGroupDto(20, 51),
                new DiceRollGroupDto(20, 50)
            ]));
    }

    [Fact]
    public void Two_groups_with_fifty_dice_are_accepted()
    {
        var rolls = _service.RollDice([
            new DiceRollGroupDto(20, 50),
            new DiceRollGroupDto(20, 50)
        ]);

        Assert.Equal(100, rolls.Length);
    }

    [Fact]
    public void One_hundred_dice_have_valid_values()
    {
        var rolls = _service.RollDice([new DiceRollGroupDto(1_000_000, 100)]);

        Assert.Equal(100, rolls.Length);
        Assert.All(rolls, roll => Assert.InRange(roll.Value, 1, 1_000_000));
    }
}
