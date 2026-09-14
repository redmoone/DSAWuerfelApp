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

    [Fact]
    public void Simple_armor_uses_its_total_rating_for_every_zone()
    {
        var set = CreateSet(
            CombatArmorModel.Simple,
            usesZonalArmor: false,
            simpleArmor: new CombatSimpleArmorDto(4, 2));

        Assert.Equal(4, CombatZoneRules.ResolveArmorRating(set, CombatArmorZone.Head));
        Assert.Equal(4, CombatZoneRules.ResolveArmorRating(set, CombatArmorZone.LeftLeg));
    }

    [Fact]
    public void Zonal_armor_keeps_asymmetric_arm_and_leg_values()
    {
        var set = CreateSet(
            CombatArmorModel.Zone,
            usesZonalArmor: true,
            armorZones: new CombatArmorZonesDto(
                Head: 5,
                Chest: 4,
                Back: 3,
                Abdomen: 2,
                LeftArm: 6,
                RightArm: 1,
                LeftLeg: 7,
                RightLeg: 0,
                Total: 4,
                TotalProtection: 4,
                TotalZoneProtection: 4,
                Encumbrance: 2));

        Assert.Equal(6, CombatZoneRules.ResolveArmorRating(set, CombatArmorZone.LeftArm));
        Assert.Equal(1, CombatZoneRules.ResolveArmorRating(set, CombatArmorZone.RightArm));
        Assert.Equal(7, CombatZoneRules.ResolveArmorRating(set, CombatArmorZone.LeftLeg));
        Assert.Equal(0, CombatZoneRules.ResolveArmorRating(set, CombatArmorZone.RightLeg));
    }

    [Fact]
    public void Missing_armor_stays_unknown()
    {
        Assert.Null(CombatZoneRules.ResolveArmorRating(null, CombatArmorZone.Chest));
        Assert.Null(CombatZoneRules.ResolveArmorRating(
            CreateSet(CombatArmorModel.Zone, usesZonalArmor: true),
            CombatArmorZone.Chest));
        Assert.Null(CombatZoneRules.ResolveArmorRating(
            CreateSet(CombatArmorModel.Simple, usesZonalArmor: false),
            CombatArmorZone.Chest));
    }

    [Fact]
    public void Resolved_zone_and_rs_can_be_stored_together_for_a_hit_snapshot()
    {
        var set = CreateSet(
            CombatArmorModel.Zone,
            usesZonalArmor: true,
            armorZones: new CombatArmorZonesDto(
                Head: 1,
                Chest: 2,
                Back: 3,
                Abdomen: 4,
                LeftArm: 5,
                RightArm: 6,
                LeftLeg: 7,
                RightLeg: 8,
                Total: 4,
                TotalProtection: 4,
                TotalZoneProtection: 4,
                Encumbrance: 1));
        var resolved = CombatZoneRules.ResolveHitZone(
            10,
            new CombatZoneRollRequestDto(CombatFacing.Front, CombatArmorZone.LeftArm, CombatArmorZone.RightArm));

        var snapshot = new CombatZoneSnapshotDto(
            10,
            resolved.ArmorZone,
            resolved.WoundZone,
            CombatFacing.Front,
            CombatZoneRules.ResolveArmorRating(set, resolved.ArmorZone));

        Assert.Equal(CombatArmorZone.RightArm, snapshot.ArmorZone);
        Assert.Equal(CombatWoundZone.RightArm, snapshot.WoundZone);
        Assert.Equal(6, snapshot.ArmorRating);
    }

    private static CombatSetVariantDto CreateSet(
        CombatArmorModel armorModel,
        bool usesZonalArmor,
        CombatArmorZonesDto? armorZones = null,
        CombatSimpleArmorDto? simpleArmor = null) =>
        new(
            "set-1",
            1,
            armorModel,
            IsInUse: true,
            IsDefault: true,
            UsesZonalArmor: usesZonalArmor,
            Dodge: null,
            SpeedWithArmor: null,
            Initiative: null,
            Raufen: null,
            Ringen: null,
            ArmorZones: armorZones,
            SimpleArmor: simpleArmor,
            Weapons: [],
            ArmorPieces: []);
}
