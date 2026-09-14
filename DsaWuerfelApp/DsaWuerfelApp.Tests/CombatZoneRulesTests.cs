using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Tests;

public sealed class CombatZoneRulesTests
{
    [Theory]
    [InlineData(1, CombatArmorZone.LeftLeg, CombatWoundZone.LeftLeg)]
    [InlineData(6, CombatArmorZone.RightLeg, CombatWoundZone.RightLeg)]
    [InlineData(7, CombatArmorZone.Abdomen, CombatWoundZone.Abdomen)]
    [InlineData(8, CombatArmorZone.Abdomen, CombatWoundZone.Abdomen)]
    [InlineData(9, CombatArmorZone.LeftArm, CombatWoundZone.LeftArm)]
    [InlineData(14, CombatArmorZone.RightArm, CombatWoundZone.RightArm)]
    [InlineData(15, CombatArmorZone.Chest, CombatWoundZone.Torso)]
    [InlineData(18, CombatArmorZone.Chest, CombatWoundZone.Torso)]
    [InlineData(19, CombatArmorZone.Head, CombatWoundZone.Head)]
    [InlineData(20, CombatArmorZone.Head, CombatWoundZone.Head)]
    public void Resolves_w20_boundaries_to_armor_and_wound_zones(
        int roll,
        CombatArmorZone expectedArmor,
        CombatWoundZone expectedWound)
    {
        var resolved = CombatZoneRules.ResolveHitZone(
            roll,
            new CombatZoneRollRequestDto(CombatFacing.Front, CombatArmorZone.LeftArm, CombatArmorZone.RightArm));

        Assert.Equal(expectedArmor, resolved.ArmorZone);
        Assert.Equal(expectedWound, resolved.WoundZone);
    }

    [Fact]
    public void Facing_changes_the_front_or_back_armor_zone_but_keeps_torso_wounds()
    {
        var front = CombatZoneRules.ResolveHitZone(
            15,
            new CombatZoneRollRequestDto(CombatFacing.Front, CombatArmorZone.LeftArm, CombatArmorZone.RightArm));
        var back = CombatZoneRules.ResolveHitZone(
            15,
            new CombatZoneRollRequestDto(CombatFacing.Back, CombatArmorZone.LeftArm, CombatArmorZone.RightArm));

        Assert.Equal(CombatArmorZone.Chest, front.ArmorZone);
        Assert.Equal(CombatArmorZone.Back, back.ArmorZone);
        Assert.Equal(CombatWoundZone.Torso, front.WoundZone);
        Assert.Equal(CombatWoundZone.Torso, back.WoundZone);
    }

    [Fact]
    public void Arm_hits_follow_the_declared_loadout_arms()
    {
        var defaultLoadout = new CombatZoneRollRequestDto(
            CombatFacing.Front,
            CombatArmorZone.LeftArm,
            CombatArmorZone.RightArm);
        var reversedLoadout = new CombatZoneRollRequestDto(
            CombatFacing.Front,
            CombatArmorZone.RightArm,
            CombatArmorZone.LeftArm);

        var defaultShieldArm = CombatZoneRules.ResolveHitZone(9, defaultLoadout);
        var defaultWeaponArm = CombatZoneRules.ResolveHitZone(10, defaultLoadout);
        var reversedShieldArm = CombatZoneRules.ResolveHitZone(9, reversedLoadout);
        var reversedWeaponArm = CombatZoneRules.ResolveHitZone(10, reversedLoadout);

        Assert.Equal(CombatArmorZone.LeftArm, defaultShieldArm.ArmorZone);
        Assert.Equal(CombatArmorZone.RightArm, defaultWeaponArm.ArmorZone);
        Assert.Equal(CombatArmorZone.RightArm, reversedShieldArm.ArmorZone);
        Assert.Equal(CombatArmorZone.LeftArm, reversedWeaponArm.ArmorZone);
    }

    [Fact]
    public void The_mapping_covers_all_seven_wound_zones()
    {
        var request = new CombatZoneRollRequestDto(
            CombatFacing.Front,
            CombatArmorZone.LeftArm,
            CombatArmorZone.RightArm);

        var zones = new[] { 1, 7, 9, 10, 15, 19, 6 }
            .Select(roll => CombatZoneRules.ResolveHitZone(roll, request).WoundZone)
            .ToHashSet();

        Assert.Equal(Enum.GetValues<CombatWoundZone>().ToHashSet(), zones);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Rejects_values_outside_a_w20(int roll)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatZoneRules.ResolveHitZone(roll, null));
    }
}
