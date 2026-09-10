namespace DsaWuerfelApp.Shared;

public sealed record DiceRollGroupDto(int Sides, int Count);

public sealed record DiceRollDto(int Sides, int Value);

public sealed record RollEquationDto(
    DiceRollGroupDto[] Dice,
    int Modifier,
    DiceRollDto[] Rolls,
    int Sum,
    int Total);

public enum RollHistoryKind
{
    Free,
    Talent,
    Spell,
    Attribute,
    BadTrait
}

public enum RollHistoryOutcome
{
    None,
    Success,
    Failure,
    CriticalSuccess,
    Fumble
}

public enum RollHistoryCheckState
{
    WithinTarget,
    Compensated,
    Failed
}

public sealed record RollHistoryCheckDto(
    string Name,
    int Roll,
    int TargetValue,
    int Difference,
    RollHistoryCheckState State,
    int? RemainingPoints = null);

public sealed record RollHistoryRequirementCheckDto(
    string Name,
    int BaseValue,
    int Roll,
    int Difference);

public sealed record RollHistorySnapshotDto
{
    public string? Probe { get; init; }
    public string? HeroName { get; init; }
    public int? TalentValue { get; init; }
    public int? EffectiveTalentValue { get; init; }
    public int? BasisModifier { get; init; }
    public int? EffectiveModifier { get; init; }
    public string? SpecializationName { get; init; }
    public int? SpecializationModifier { get; init; }
    public string? SchlechteEigenschaftName { get; init; }
    public int? SchlechteEigenschaftModifier { get; init; }
    public int? SuccessCount { get; init; }
    public int? FailureCount { get; init; }
    public int? Margin { get; init; }
    public string? EigenschaftName { get; init; }
    public int? EigenschaftWert { get; init; }
    public int? TargetValue { get; init; }
    public bool? EigenschaftSetztSichDurch { get; init; }
    public int? RequiredTalentValue { get; init; }
    public int? RequiredCompensation { get; init; }
    public RollHistoryRequirementCheckDto[] RequirementChecks { get; init; } = [];
    public int? OriginalZfw { get; init; }
    public int? AutomaticModifier { get; init; }
    public int? ManualModifier { get; init; }
    public int? PreRollZfp { get; init; }
    public int? RawZfp { get; init; }
    public int? AvailableZfp { get; init; }
    public bool? ManualModifierRequired { get; init; }
    public string[] SelectedOptions { get; init; } = [];
}

public sealed record RollHistoryContextDto(
    RollHistoryKind Kind,
    string DisplayName,
    RollHistoryOutcome Outcome,
    int? RemainingPoints,
    RollHistoryCheckDto[] Checks,
    RollHistorySnapshotDto? Snapshot = null);

public sealed record RollHistoryEntryDto(
    string PlayerName,
    DateTime Timestamp,
    DiceRollDto[] Rolls,
    int Modifier,
    int TotalSum,
    RollHistoryContextDto? Context = null);

public sealed record ProbeSearchAlternativeDto(string Label, string Value);

public sealed record ProbeSearchEntryDto(
    string DisplayLabel,
    string? Value,
    bool IsSelectable,
    ProbeSearchAlternativeDto[] Alternatives);

public sealed record AttributeValueDto(string Name, int Value);

public sealed record BadTraitDto(string Name, int Value, int TalentModifier, int AttributeModifier);

public sealed record DicePageContextDto(
    Guid? ActiveHeroId,
    string? ActiveHeroName,
    AttributeValueDto[] Attributes,
    ProbeSearchEntryDto[] AvailableProbes,
    BadTraitDto[] BadTraits,
    string ProbePlaceholder,
    bool ShowDebugForcedRolls);

public sealed record ProbeInfoRequestDto(
    string? SessionId,
    Guid? HeroId,
    string ProbeValue,
    int Modifier,
    string? BadTraitName,
    string[] SpellOptionValues);

public sealed record ProbeInfoSectionDto(string Label, string Text);

public sealed record SpellOptionButtonDto(
    string Label,
    string Value,
    bool IsSelected,
    bool IsDisabled,
    string? Description);

public sealed record SpellOptionGroupDto(string Label, SpellOptionButtonDto[] Options);

public sealed record SpellSelectionPanelDto(
    SpellOptionGroupDto[] Groups,
    string? SimultaneousModificationNote,
    int? MaximumSelectableOptions,
    int SelectedOptionCount);

public sealed record ProbeInfoResultDto(
    string? SummaryText,
    string? DetailsText,
    ProbeInfoSectionDto[] Sections,
    SpellSelectionPanelDto? SpellSelection,
    ProbeSelectionKind Kind = ProbeSelectionKind.Unknown);

public sealed record FreeRollRequestDto(
    string? SessionId,
    DiceRollGroupDto[] Dice,
    int Modifier,
    bool IsHidden);

public sealed record FreeRollResultDto(
    string PlayerName,
    DateTime Timestamp,
    RollEquationDto Equation,
    RollHistoryEntryDto HistoryEntry);

public sealed record TalentRollRequestDto(
    string? SessionId,
    Guid? HeroId,
    string TalentKey,
    int Modifier,
    string? BadTraitName,
    string[] SpellOptionValues,
    string? ForcedRollsText,
    bool IsHidden);

public sealed record TalentRollDetailDto(
    string Attribute,
    int BaseValue,
    int TargetValue,
    int Roll,
    int Difference,
    int RemainingRest,
    bool Success);

public sealed record SpellProbeRollDetailsDto(
    int OriginalZfw,
    int AutomaticModifier,
    int ManualModifier,
    int PreRollZfp,
    int RawZfp,
    int AvailableZfp,
    bool ManualModifierRequired,
    string[] SelectedOptions);

public sealed record TalentRollResultDto(
    string PlayerName,
    DateTime Timestamp,
    string TalentName,
    int TalentValue,
    string Probe,
    int Modifier,
    int BasisModifier,
    string? SpecializationName,
    int SpecializationModifier,
    string? SchlechteEigenschaftName,
    int SchlechteEigenschaftModifier,
    int EffectiveTalentValue,
    DiceRollDto[] Rolls,
    TalentRollDetailDto[] Details,
    TalentProbeStatus Status,
    int Rest,
    bool Success,
    int Margin,
    RollEquationDto Equation,
    RollHistoryEntryDto HistoryEntry)
{
    public SpellProbeRollDetailsDto? SpellDetails { get; init; }
}

public sealed record AttributeRollRequestDto(
    string? SessionId,
    Guid? HeroId,
    string[] Attributes,
    int Modifier,
    string? BadTraitName,
    bool IsHidden);

public sealed record AttributeRollDetailDto(
    string Attribute,
    int BaseValue,
    int TargetValue,
    int Roll,
    int Difference,
    bool Success);

public sealed record AttributeRollRequirementDetailDto(
    string Attribute,
    int BaseValue,
    int Roll,
    int Difference);

public sealed record AttributeRollRequirementDto(
    string Probe,
    int BasisModifier,
    string? SchlechteEigenschaftName,
    int SchlechteEigenschaftModifier,
    int EffectiveModifier,
    int RequiredTalentValue,
    int RequiredCompensation,
    AttributeRollRequirementDetailDto[] Details);

public sealed record AttributeRollResultDto(
    string PlayerName,
    DateTime Timestamp,
    string Probe,
    int BasisModifier,
    string? SchlechteEigenschaftName,
    int SchlechteEigenschaftModifier,
    int EffectiveModifier,
    bool Success,
    int SuccessCount,
    int FailureCount,
    AttributeRollDetailDto[] Details,
    AttributeRollRequirementDto? Requirement,
    DiceRollDto[] Rolls,
    RollEquationDto Equation,
    RollHistoryEntryDto HistoryEntry);

public sealed record BadTraitRollRequestDto(
    string? SessionId,
    Guid? HeroId,
    string BadTraitName,
    int BadTraitValue,
    string? ForcedRollsText,
    bool IsHidden);

public sealed record BadTraitRollResultDto(
    string PlayerName,
    DateTime Timestamp,
    string EigenschaftName,
    int EigenschaftWert,
    int TargetValue,
    DiceRollDto Roll,
    SchlechteEigenschaftProbeStatus Status,
    bool Success,
    bool EigenschaftSetztSichDurch,
    int Margin,
    RollEquationDto Equation,
    RollHistoryEntryDto HistoryEntry);

public sealed record SessionConnectionDto(string SessionId, string JoinCode);

public sealed record MasterRollTargetDto(
    string UserId,
    string PlayerName,
    Guid HeroId,
    string? HeroName);

public sealed record MasterTalentRollRequestDto(
    string SessionId,
    MasterRollTargetDto[] Targets,
    string TalentKey,
    int Modifier,
    string? BadTraitName,
    string[] SpellOptionValues,
    string? ForcedRollsText);

public sealed record MasterTalentRollTargetResultDto(
    string UserId,
    string PlayerName,
    Guid HeroId,
    string? HeroName,
    TalentRollResultDto? Result,
    AttributeRollResultDto? RequirementResult,
    string? ErrorMessage);

public sealed record MasterAttributeRollRequestDto(
    string SessionId,
    MasterRollTargetDto[] Targets,
    string[] Attributes,
    int Modifier,
    string? BadTraitName);

public sealed record MasterAttributeRollTargetResultDto(
    string UserId,
    string PlayerName,
    Guid HeroId,
    string? HeroName,
    AttributeRollResultDto? Result,
    string? ErrorMessage);
