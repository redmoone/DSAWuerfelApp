namespace DsaWuerfelApp.Shared;

public enum CombatActionKind
{
    MeleeAttack,
    WeaponParry,
    ShieldParry,
    Dodge,
    RangedAttack,
    Damage,
    HitZone,
    InitiativeHelper,
    WoundHelper,
    FumbleHelper
}

public enum CombatOutcome
{
    Unknown,
    Neutral,
    Success,
    Failure,
    Lucky,
    Critical,
    FumbleAvoided,
    Fumble
}

public enum CombatFollowUpKind
{
    AdditionalStructurePoints,
    UnconsciousDuration,
    Bleeding,
    InitiativeLoss,
    FumbleTable
}

public sealed record CombatModifierDto(string Label, int Value, string? Source = null);

public sealed record CombatRuleOptionsDto(
    bool SpecialResultsEnabled = true,
    bool LowLePEnabled = false);

public sealed record CombatRuntimeStateDto
{
    public bool IsStarted { get; init; }
    public int? CurrentLeP { get; init; }
    public int? CurrentAuP { get; init; }
    public Dictionary<CombatWoundZone, int?> Wounds { get; init; } = [];
}

public sealed record CombatRuntimeModifierResult(
    CombatModifierDto[] Modifiers,
    string[] RuleNotes);

public sealed record CombatHelperRollRequestDto
{
    public int DiceCount { get; init; } = 1;
    public int DiceSides { get; init; } = 6;
    public string Purpose { get; init; } = string.Empty;
}

public sealed record CombatDamageRollRequestDto
{
    public int DiceCount { get; init; }
    public int DiceSides { get; init; } = 6;
    public int WeaponBonus { get; init; }
    public int PreMultiplierModifier { get; init; }
    public int Multiplier { get; init; } = 1;
    public int PostMultiplierModifier { get; init; }
    public bool IsCritical { get; init; }
}

public sealed record CombatZoneRollRequestDto(
    CombatFacing Facing,
    CombatArmorZone ShieldArm,
    CombatArmorZone SwordArm);

public sealed record CombatRollRequestDto
{
    public Guid RequestId { get; init; }
    public string? SessionId { get; init; }
    public Guid? HeroId { get; init; }
    public string? SetId { get; init; }
    public CombatActionKind Action { get; init; }
    public string? WeaponId { get; init; }
    public string? WeaponName { get; init; }
    public CombatRuntimeStateDto? RuntimeState { get; init; }
    public int? BaseValue { get; init; }
    public int? UnmodifiedBaseValue { get; init; }
    public CombatModifierDto[] Modifiers { get; init; } = [];
    public CombatRuleOptionsDto Options { get; init; } = new();
    public CombatDamageRollRequestDto? Damage { get; init; }
    public int DamageModifier { get; init; }
    public CombatZoneRollRequestDto? Zone { get; init; }
    public CombatHelperRollRequestDto? Helper { get; init; }
    public string? Note { get; init; }
}

public sealed record CombatLabeledRollDto(string Role, int Sides, int Value);

public sealed record CombatDamageSnapshotDto(
    int DiceTotal,
    int WeaponBonus,
    int PreMultiplierModifier,
    int Multiplier,
    int PostMultiplierModifier,
    int Total,
    bool IsCritical);

public sealed record CombatZoneSnapshotDto(
    int? W20,
    CombatArmorZone? ArmorZone,
    CombatWoundZone? WoundZone,
    CombatFacing? Facing,
    int? ArmorRating);

public sealed record CombatRollSnapshotDto
{
    public Guid EntryId { get; init; }
    public Guid RequestId { get; init; }
    public string? SessionId { get; init; }
    public Guid? HeroId { get; init; }
    public CombatActionKind Action { get; init; }
    public string? ActionLabel { get; init; }
    public string? WeaponName { get; init; }
    public string ValuesSource { get; init; } = "Import";
    public int? BaseValue { get; init; }
    public int? UnmodifiedBaseValue { get; init; }
    public int? EffectiveTarget { get; init; }
    public int? ControlTarget { get; init; }
    public CombatModifierDto[] Modifiers { get; init; } = [];
    public CombatRuleOptionsDto RuleOptions { get; init; } = new();
    public CombatOutcome Outcome { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public CombatLabeledRollDto[] LabeledRolls { get; init; } = [];
    public CombatDamageSnapshotDto? Damage { get; init; }
    public CombatZoneSnapshotDto? Zone { get; init; }
    public string[] RuleNotes { get; init; } = [];
}

public sealed record CombatRollResultDto(
    Guid EntryId,
    Guid RequestId,
    string? SessionId,
    string ActorUserId,
    string PlayerName,
    string? HeroName,
    CombatOutcome Outcome,
    CombatRollSnapshotDto Snapshot,
    DiceRollDto[] Rolls,
    RollHistoryEntryDto HistoryEntry);

public sealed record CombatRollEvaluationDto(
    bool IsValid,
    string? ValidationMessage,
    CombatActionKind Action,
    int? BaseValue,
    int? EffectiveTarget,
    int? ControlTarget,
    int MainRoll,
    int? ControlRoll,
    CombatOutcome Outcome,
    bool IsSuccessful,
    bool IsCritical,
    bool IsFumble,
    bool RequiresDefenseDecision,
    string StatusLabel);

public sealed record CombatDamageCalculationDto(
    int DiceTotal,
    int WeaponBonus,
    int PreMultiplierModifier,
    int Multiplier,
    int PostMultiplierModifier,
    int Total,
    bool IsCritical);

public sealed record CombatWoundThresholdsDto(int? First, int? Second, int? Third);

public sealed record CombatFollowUpRequirementDto(
    string Id,
    CombatFollowUpKind Kind,
    int DiceCount,
    int DiceSides,
    string Purpose,
    bool RequiresConfirmation = true);
