using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelState
{
    public WuerfelViewState Current { get; private set; } = WuerfelViewState.Empty;

    public event Action? Changed;

    public void ApplyContext(DicePageContextDto context)
    {
        var selectedBadTraitName = context.BadTraits.Any(trait =>
            string.Equals(trait.Name, Current.SelectedBadTraitName, StringComparison.Ordinal))
            ? Current.SelectedBadTraitName
            : null;

        Update(Current with
        {
            ActiveHeroId = context.ActiveHeroId,
            ActiveHeroName = context.ActiveHeroName,
            AttributeValues =
            context.Attributes.ToDictionary(attribute => attribute.Name, attribute => attribute.Value),
            AvailableProbes = context.AvailableProbes,
            BadTraits = context.BadTraits,
            ProbePlaceholder = context.ProbePlaceholder,
            ShowDebugForcedRolls = context.ShowDebugForcedRolls,
            SelectedAttributes = Array.Empty<string>(),
            SelectedDiceSides = Array.Empty<int>(),
            SelectedProbeValue = null,
            SelectedBadTraitName = selectedBadTraitName,
            Modifier = 0,
            RollText = string.Empty,
            ForcedRollsText = string.Empty,
            ProbeInfo = null,
            ErrorMessage = null,
            ActiveArea = WuerfelArea.None,
            LastTalentRoll = null,
            LastAttributeRoll = null,
            LastBadTraitRoll = null,
            PreviewVersion = Current.PreviewVersion + 1
        });
    }

    public void BeginLoading()
    {
        Update(Current with { IsBusy = true, ErrorMessage = null });
    }

    public void EndLoading()
    {
        Update(Current with { IsBusy = false });
    }

    public void SetError(string? errorMessage)
    {
        Update(Current with { ErrorMessage = errorMessage });
    }

    public void ToggleHiddenRoll()
    {
        Update(Current with { IsHiddenRoll = !Current.IsHiddenRoll });
    }

    public void SetModifier(int modifier)
    {
        Update(Current with { Modifier = modifier, SelectedBadTraitName = null, ErrorMessage = null });
    }

    public void SetRollText(string rollText)
    {
        Update(Current with { RollText = rollText, ErrorMessage = null });
    }

    public void SetForcedRollsText(string forcedRollsText)
    {
        Update(Current with { ForcedRollsText = forcedRollsText, ErrorMessage = null });
    }

    public void SetSelectedBadTrait(string? selectedBadTraitName)
    {
        var nextSelection = string.IsNullOrWhiteSpace(selectedBadTraitName) ||
                            !Current.BadTraits.Any(trait =>
                                string.Equals(trait.Name, selectedBadTraitName, StringComparison.Ordinal))
            ? null
            : selectedBadTraitName;

        Update(Current with { SelectedBadTraitName = nextSelection, ErrorMessage = null });
    }

    public void SwitchArea(WuerfelArea targetArea)
    {
        if (Current.ActiveArea == targetArea)
        {
            return;
        }

        var nextState = Current;

        if (Current.ActiveArea != WuerfelArea.None)
        {
            nextState = ClearRollArea(nextState, targetArea != WuerfelArea.ProbeSearch);
        }

        Update(nextState with { ActiveArea = targetArea });
    }

    public void ResetRollArea()
    {
        Update(ClearRollArea(Current, true) with { ActiveArea = WuerfelArea.None });
    }

    public void SetSelectedDice(IReadOnlyList<int> selectedDiceSides)
    {
        Update(Current with
        {
            SelectedDiceSides = selectedDiceSides.ToArray(),
            ErrorMessage = null,
            PreviewVersion = Current.PreviewVersion + 1
        });
    }

    public void SetSelectedAttributes(IReadOnlyList<string> selectedAttributes, IReadOnlyList<int> selectedDiceSides)
    {
        Update(Current with
        {
            SelectedAttributes = selectedAttributes.ToArray(),
            SelectedDiceSides = selectedDiceSides.ToArray(),
            ErrorMessage = null,
            PreviewVersion = Current.PreviewVersion + 1
        });
    }

    public void SetSelectedProbe(string? selectedProbeValue)
    {
        Update(Current with
        {
            SelectedProbeValue = string.IsNullOrWhiteSpace(selectedProbeValue) ? null : selectedProbeValue,
            ProbeInfo = null,
            ErrorMessage = null,
            ActiveArea = string.IsNullOrWhiteSpace(selectedProbeValue) && Current.ActiveArea == WuerfelArea.ProbeSearch
                ? WuerfelArea.None
                : Current.ActiveArea
        });
    }

    public void SetProbeInfo(ProbeInfoResultDto? probeInfo)
    {
        Update(Current with { ProbeInfo = probeInfo, ErrorMessage = null });
    }

    public void SetHistory(IReadOnlyList<RollHistoryEntryDto> history)
    {
        Update(Current with { History = history.ToArray() });
    }

    public void ApplyFreeRollResult(FreeRollResultDto result)
    {
        ApplyResult(result.Equation, result.HistoryEntry, null, null, null);
    }

    public void ApplyTalentRollResult(TalentRollResultDto result)
    {
        ApplyResult(result.Equation, result.HistoryEntry, result, null, null);
    }

    public void ApplyAttributeRollResult(AttributeRollResultDto result)
    {
        ApplyResult(result.Equation, result.HistoryEntry, null, result, null);
    }

    public void ApplyBadTraitRollResult(BadTraitRollResultDto result)
    {
        ApplyResult(result.Equation, result.HistoryEntry, null, null, result);
    }

    private void ApplyResult(
        RollEquationDto equation,
        RollHistoryEntryDto historyEntry,
        TalentRollResultDto? talentRollResult,
        AttributeRollResultDto? attributeRollResult,
        BadTraitRollResultDto? badTraitRollResult)
    {
        var history = Current.History.Prepend(historyEntry).Take(100).ToArray();

        Update(Current with
        {
            LastTalentRoll = talentRollResult,
            LastAttributeRoll = attributeRollResult,
            LastBadTraitRoll = badTraitRollResult,
            History = history,
            AnimatedDiceSides = equation.Rolls.Select(roll => roll.Sides).ToArray(),
            AnimatedDiceValues = equation.Rolls.Select(roll => roll.Value).ToArray(),
            ResultVersion = Current.ResultVersion + 1,
            ErrorMessage = null
        });
    }

    private static WuerfelViewState ClearRollArea(WuerfelViewState state, bool clearSelectedProbe)
    {
        return state with
        {
            SelectedAttributes = Array.Empty<string>(),
            SelectedDiceSides = Array.Empty<int>(),
            SelectedProbeValue = clearSelectedProbe ? null : state.SelectedProbeValue,
            Modifier = 0,
            RollText = string.Empty,
            ForcedRollsText = string.Empty,
            ProbeInfo = clearSelectedProbe ? null : state.ProbeInfo,
            ErrorMessage = null,
            LastTalentRoll = null,
            LastAttributeRoll = null,
            LastBadTraitRoll = null,
            PreviewVersion = state.PreviewVersion + 1
        };
    }

    private void Update(WuerfelViewState nextState)
    {
        Current = nextState;
        Changed?.Invoke();
    }
}

public enum WuerfelArea
{
    None,
    Attributes,
    ProbeSearch,
    FreeRoll
}

public sealed record WuerfelViewState
{
    public static WuerfelViewState Empty { get; } = new();

    public Guid? ActiveHeroId { get; init; }
    public string? ActiveHeroName { get; init; }
    public IReadOnlyDictionary<string, int> AttributeValues { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<ProbeSearchEntryDto> AvailableProbes { get; init; } = Array.Empty<ProbeSearchEntryDto>();
    public IReadOnlyList<BadTraitDto> BadTraits { get; init; } = Array.Empty<BadTraitDto>();
    public string ProbePlaceholder { get; init; } = "Nach Proben suchen...";
    public IReadOnlyList<string> SelectedAttributes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> SelectedDiceSides { get; init; } = Array.Empty<int>();
    public string? SelectedProbeValue { get; init; }
    public string? SelectedBadTraitName { get; init; }
    public int Modifier { get; init; }
    public string RollText { get; init; } = string.Empty;
    public bool IsHiddenRoll { get; init; }
    public string ForcedRollsText { get; init; } = string.Empty;
    public ProbeInfoResultDto? ProbeInfo { get; init; }
    public bool ShowDebugForcedRolls { get; init; }
    public bool IsBusy { get; init; }
    public string? ErrorMessage { get; init; }
    public WuerfelArea ActiveArea { get; init; }
    public TalentRollResultDto? LastTalentRoll { get; init; }
    public AttributeRollResultDto? LastAttributeRoll { get; init; }
    public BadTraitRollResultDto? LastBadTraitRoll { get; init; }
    public IReadOnlyList<RollHistoryEntryDto> History { get; init; } = Array.Empty<RollHistoryEntryDto>();
    public IReadOnlyList<int> AnimatedDiceSides { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> AnimatedDiceValues { get; init; } = Array.Empty<int>();
    public long PreviewVersion { get; init; }
    public long ResultVersion { get; init; }

    public bool HasActiveHero => ActiveHeroId.HasValue;

    public int ActiveBadTraitModifier => ActiveArea switch
    {
        WuerfelArea.ProbeSearch => SelectedBadTrait?.TalentModifier ?? 0,
        WuerfelArea.Attributes => SelectedBadTrait?.AttributeModifier ?? 0,
        _ => 0
    };

    public int ActiveTalentSpecializationModifier =>
        ActiveArea == WuerfelArea.ProbeSearch
            ? TalentSelectionValue.Parse(SelectedProbeValue).SpecializationModifier
            : 0;

    public int EffectiveModifier => Modifier + ActiveBadTraitModifier + ActiveTalentSpecializationModifier;

    public bool CanRoll =>
        SelectedDiceSides.Count > 0 ||
        SelectedAttributes.Count > 0 ||
        !string.IsNullOrWhiteSpace(SelectedProbeValue);

    private BadTraitDto? SelectedBadTrait =>
        string.IsNullOrWhiteSpace(SelectedBadTraitName)
            ? null
            : BadTraits.FirstOrDefault(trait =>
                string.Equals(trait.Name, SelectedBadTraitName, StringComparison.Ordinal));
}