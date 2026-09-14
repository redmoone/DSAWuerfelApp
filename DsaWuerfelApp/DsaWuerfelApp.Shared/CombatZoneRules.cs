namespace DsaWuerfelApp.Shared;

public static class CombatZoneRules
{
    public static (CombatArmorZone ArmorZone, CombatWoundZone WoundZone) ResolveHitZone(
        int value,
        CombatZoneRollRequestDto? request)
    {
        if (value is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Ein Trefferzonenwurf muss zwischen 1 und 20 liegen.");
        }

        var facing = request?.Facing == CombatFacing.Back
            ? CombatFacing.Back
            : CombatFacing.Front;
        var shieldArm = request?.ShieldArm is CombatArmorZone.LeftArm or CombatArmorZone.RightArm
            ? request.ShieldArm
            : CombatArmorZone.LeftArm;
        var swordArm = request?.SwordArm is CombatArmorZone.LeftArm or CombatArmorZone.RightArm
            ? request.SwordArm
            : CombatArmorZone.RightArm;

        var armor = value switch
        {
            <= 6 => value % 2 == 1 ? CombatArmorZone.LeftLeg : CombatArmorZone.RightLeg,
            <= 8 => CombatArmorZone.Abdomen,
            <= 14 => value % 2 == 1 ? shieldArm : swordArm,
            <= 18 => facing == CombatFacing.Front ? CombatArmorZone.Chest : CombatArmorZone.Back,
            _ => CombatArmorZone.Head
        };

        var wound = armor switch
        {
            CombatArmorZone.Head => CombatWoundZone.Head,
            CombatArmorZone.Chest or CombatArmorZone.Back => CombatWoundZone.Torso,
            CombatArmorZone.Abdomen => CombatWoundZone.Abdomen,
            CombatArmorZone.LeftArm => CombatWoundZone.LeftArm,
            CombatArmorZone.RightArm => CombatWoundZone.RightArm,
            CombatArmorZone.LeftLeg => CombatWoundZone.LeftLeg,
            _ => CombatWoundZone.RightLeg
        };

        return (armor, wound);
    }
}
