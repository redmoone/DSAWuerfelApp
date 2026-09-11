namespace DsaWuerfelApp.Shared;

public enum CombatArmorModel
{
    Unknown,
    Zone,
    Simple
}

public enum CombatWeaponCategory
{
    Melee,
    Ranged,
    Shield,
    Unarmed
}

public enum CombatArmorZone
{
    Head,
    Chest,
    Back,
    Abdomen,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg
}

public enum CombatWoundZone
{
    Head,
    Torso,
    Abdomen,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg
}

public enum CombatFacing
{
    Front,
    Back
}

public sealed record CombatProfileDto(
    Guid HeroId,
    string HeroName,
    int SourceRevision,
    CombatResourcesDto Resources,
    int? WoundThreshold,
    CombatAttributeDto[] Attributes,
    CombatSetVariantDto[] Sets,
    CombatSpecializationDto[] Specializations,
    CombatBenefitDto[] Benefits);

public sealed record CombatResourcesDto(
    int? LeP,
    int? AuP,
    int? AeP,
    int? KeP);

public sealed record CombatAttributeDto(string Key, string Label, int? Value, string IconPath);

public sealed record CombatSetVariantDto(
    string Id,
    int Number,
    CombatArmorModel ArmorModel,
    bool IsInUse,
    bool IsDefault,
    bool UsesZonalArmor,
    int? Dodge,
    int? SpeedWithArmor,
    int? Initiative,
    CombatUnarmedProfileDto? Raufen,
    CombatUnarmedProfileDto? Ringen,
    CombatArmorZonesDto? ArmorZones,
    CombatSimpleArmorDto? SimpleArmor,
    CombatWeaponDto[] Weapons,
    CombatArmorPieceDto[] ArmorPieces);

public sealed record CombatUnarmedProfileDto(int? Attack, int? Parry, string? Damage);

public sealed record CombatArmorZonesDto(
    int? Head,
    int? Chest,
    int? Back,
    int? Abdomen,
    int? LeftArm,
    int? RightArm,
    int? LeftLeg,
    int? RightLeg,
    int? Total,
    int? TotalProtection,
    int? TotalZoneProtection,
    int? Encumbrance);

public sealed record CombatSimpleArmorDto(int? Total, int? Encumbrance);

public sealed record CombatArmorPieceDto(
    int? Number,
    string Name,
    string? Basis,
    string? RawRs,
    string? RawBe,
    int? Head,
    int? Chest,
    int? Back,
    int? Abdomen,
    int? LeftArm,
    int? RightArm,
    int? LeftLeg,
    int? RightLeg,
    int? Total,
    int? TotalZoneProtection,
    int? Encumbrance);

public sealed record CombatWeaponDto(
    string Id,
    CombatWeaponCategory Category,
    int? Number,
    string Name,
    bool? IsAvailable,
    int? Attack,
    int? Parry,
    int? RangedValue,
    string? BaseDamage,
    string? CalculatedDamage,
    string? WeaponModifier,
    int? Initiative,
    string? DistanceClass,
    string? DamageThreshold,
    string? WeaponTalent,
    string? Encumbrance,
    int[] RangeBands,
    int[] RangeDamageModifiers,
    int? ReloadTime,
    string? ShieldType,
    string? ShieldModifier,
    string? BreakageMinimum,
    string? BreakageCurrent,
    string? Breakage);

public sealed record CombatSpecializationDto(string Name, string? Identifier, string[] Categories, bool IsLearned);

public sealed record CombatBenefitDto(string Name, string? Identifier);
