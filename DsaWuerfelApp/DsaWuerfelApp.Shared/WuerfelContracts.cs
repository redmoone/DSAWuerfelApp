namespace DsaWuerfelApp.Shared;

public sealed record DiceRollGroupDto(int Sides, int Count);

public sealed record DiceRollDto(int Sides, int Value);

public sealed record RollEquationDto(
    DiceRollGroupDto[] Dice,
    int Modifier,
    DiceRollDto[] Rolls,
    int Sum,
    int Total);

public sealed record RollHistoryEntryDto(
    string PlayerName,
    DateTime Timestamp,
    DiceRollDto[] Rolls,
    int Modifier,
    int TotalSum);

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
    SpellSelectionPanelDto? SpellSelection);

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
    Guid HeroId,
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
    RollHistoryEntryDto HistoryEntry);

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
    Guid HeroId,
    string BadTraitName,
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
