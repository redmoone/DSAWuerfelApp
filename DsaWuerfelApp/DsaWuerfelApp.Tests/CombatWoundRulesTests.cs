using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Tests;

public sealed class CombatWoundRulesTests
{
    private static readonly CombatWoundThresholdsDto Thresholds = new(4, 8, 12);

    [Theory]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(8, 1)]
    [InlineData(9, 2)]
    [InlineData(12, 2)]
    [InlineData(13, 3)]
    public void Applies_wounds_only_after_a_threshold_is_exceeded(
        int structurePoints,
        int expectedWounds)
    {
        var result = CombatWoundRules.Resolve(
            structurePoints,
            currentLeP: 20,
            zone: CombatWoundZone.Torso,
            currentWounds: 0,
            Thresholds);

        Assert.Equal(expectedWounds, result.ResultingWounds);
        Assert.Equal(expectedWounds, result.AddedWounds);
    }

    [Fact]
    public void New_hit_wounds_are_added_to_existing_wounds()
    {
        var result = CombatWoundRules.Resolve(
            structurePoints: 8,
            currentLeP: 20,
            zone: CombatWoundZone.LeftArm,
            currentWounds: 2,
            Thresholds);

        Assert.Equal(3, result.ResultingWounds);
        Assert.Equal(1, result.AddedWounds);
    }

    [Fact]
    public void A_later_threshold_adds_only_the_missing_wounds()
    {
        var result = CombatWoundRules.Resolve(
            structurePoints: 12,
            currentLeP: 20,
            zone: CombatWoundZone.RightLeg,
            currentWounds: 1,
            Thresholds);

        Assert.Equal(3, result.ResultingWounds);
        Assert.Equal(2, result.AddedWounds);
    }

    [Theory]
    [InlineData(0, -4)]
    [InlineData(-2, -6)]
    public void LeP_can_reach_zero_or_become_negative(
        int currentLeP,
        int expectedLeP)
    {
        var result = CombatWoundRules.Resolve(
            structurePoints: 4,
            currentLeP,
            CombatWoundZone.Head,
            currentWounds: 0,
            Thresholds);

        Assert.Equal(expectedLeP, result.LePAfter);
        Assert.True(result.IsIncapacitated);
    }

    [Fact]
    public void Missing_damage_or_wound_state_does_not_create_a_fictional_result()
    {
        var missingDamage = CombatWoundRules.Resolve(
            structurePoints: null,
            currentLeP: 20,
            zone: CombatWoundZone.Torso,
            currentWounds: 0,
            Thresholds);
        var missingWounds = CombatWoundRules.Resolve(
            structurePoints: 12,
            currentLeP: 20,
            zone: CombatWoundZone.Torso,
            currentWounds: null,
            Thresholds);

        Assert.Equal(20, missingDamage.LePAfter);
        Assert.Equal(0, missingDamage.ResultingWounds);
        Assert.Null(missingWounds.ResultingWounds);
        Assert.Equal(0, missingWounds.AddedWounds);
    }

    [Fact]
    public void Threshold_builder_uses_wound_threshold_ko_and_one_and_a_half_times_ko()
    {
        var thresholds = CombatWoundRules.CreateThresholds(woundThreshold: 4, constitution: 8);

        Assert.Equal(new CombatWoundThresholdsDto(4, 8, 12), thresholds);
    }

    [Fact]
    public void Repeated_structure_points_represent_a_new_hit_and_are_not_silently_deduplicated()
    {
        var first = CombatWoundRules.Resolve(12, 20, CombatWoundZone.Torso, 0, Thresholds);
        var repeated = CombatWoundRules.Resolve(
            first.StructurePoints,
            first.LePAfter,
            first.Zone,
            first.ResultingWounds,
            Thresholds);

        Assert.Equal(2, first.ResultingWounds);
        Assert.Equal(3, repeated.ResultingWounds);
        Assert.Equal(1, repeated.AddedWounds);
    }
}
