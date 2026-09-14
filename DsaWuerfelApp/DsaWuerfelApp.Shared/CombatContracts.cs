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

public enum CombatExchangeStatus
{
    Declared,
    AttackOpen,
    DefenseOpen,
    Hit,
    Avoided,
    DamageOpen,
    Completed,
    Cancelled
}

public static class CombatActionBudgetRules
{
    public static bool RequiresNormalAction(CombatActionKind action) => action is
        CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack;

    public static bool RequiresReaction(CombatActionKind action) => action is
        CombatActionKind.WeaponParry or CombatActionKind.ShieldParry or CombatActionKind.Dodge;
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

public sealed record CombatRuntimeInitiativeResult(
    int Modifier,
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
    public string? ExchangeId { get; init; }
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

public sealed record CombatAttackExchangeDto
{
    public string ExchangeId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public Guid RequestId { get; init; }
    public long Revision { get; init; }
    public string AttackerParticipantId { get; init; } = string.Empty;
    public string TargetParticipantId { get; init; } = string.Empty;
    public int Round { get; init; } = 1;
    public int? PhaseInitiative { get; init; }
    public string? SetId { get; init; }
    public string? WeaponId { get; init; }
    public string? WeaponName { get; init; }
    public CombatActionKind AttackKind { get; init; }
    public CombatExchangeStatus Status { get; init; } = CombatExchangeStatus.Declared;
    public bool ActionConsumed { get; init; }
    public CombatRollEvaluationDto? AttackResult { get; init; }
    public CombatActionKind[] AllowedDefenseActions { get; init; } = [];
    public CombatActionKind? SelectedDefenseAction { get; init; }
    public CombatRollEvaluationDto? DefenseResult { get; init; }
    public CombatZoneSnapshotDto? Zone { get; init; }
    public CombatDamageSnapshotDto? Damage { get; init; }
    public Guid[] HistoryEntryIds { get; init; } = [];
    public string? RuleNote { get; init; }
}

public static class CombatAttackExchangeRules
{
    public static bool HasValidIdentity(CombatAttackExchangeDto exchange) =>
        !string.IsNullOrWhiteSpace(exchange.ExchangeId) &&
        !string.IsNullOrWhiteSpace(exchange.SessionId) &&
        exchange.RequestId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(exchange.AttackerParticipantId) &&
        !string.IsNullOrWhiteSpace(exchange.TargetParticipantId) &&
        !string.Equals(exchange.AttackerParticipantId, exchange.TargetParticipantId, StringComparison.Ordinal);

    public static bool CanTransition(CombatExchangeStatus current, CombatExchangeStatus next) =>
        (current, next) switch
        {
            (CombatExchangeStatus.Declared, CombatExchangeStatus.AttackOpen or CombatExchangeStatus.Cancelled) => true,
            (CombatExchangeStatus.AttackOpen, CombatExchangeStatus.DefenseOpen or CombatExchangeStatus.Completed or CombatExchangeStatus.Cancelled) => true,
            (CombatExchangeStatus.DefenseOpen, CombatExchangeStatus.Hit or CombatExchangeStatus.Avoided or CombatExchangeStatus.Cancelled) => true,
            (CombatExchangeStatus.Hit, CombatExchangeStatus.DamageOpen or CombatExchangeStatus.Completed or CombatExchangeStatus.Cancelled) => true,
            (CombatExchangeStatus.DamageOpen, CombatExchangeStatus.Completed or CombatExchangeStatus.Cancelled) => true,
            (CombatExchangeStatus.Avoided, CombatExchangeStatus.Completed or CombatExchangeStatus.Cancelled) => true,
            _ => false
        };
}

public static class CombatTargetRules
{
    public static bool IsValidTarget(
        CombatSessionParticipantDto attacker,
        CombatSessionParticipantDto target) =>
        !string.IsNullOrWhiteSpace(attacker.Id) &&
        !string.IsNullOrWhiteSpace(target.Id) &&
        !string.Equals(attacker.Id, target.Id, StringComparison.Ordinal) &&
        target.Kind switch
        {
            CombatParticipantKind.Hero => target.HeroId.HasValue,
            CombatParticipantKind.Opponent => target.OpponentProfile?.HasBasicCombatValues == true,
            _ => false
        };
}

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

public sealed record CombatAttackDecisionDto(
    bool IsValid,
    string? ValidationMessage,
    CombatExchangeStatus Status,
    CombatActionKind AttackAction,
    CombatActionKind[] AllowedDefenseActions,
    bool IsHit,
    string StatusLabel);

public sealed record CombatDefenseDecisionDto(
    bool IsValid,
    string? ValidationMessage,
    CombatExchangeStatus Status,
    CombatActionKind DefenseAction,
    bool IsHit,
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
