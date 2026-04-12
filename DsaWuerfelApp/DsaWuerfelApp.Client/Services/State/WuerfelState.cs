using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelState
{
    public WuerfelViewState Current { get; private set; } = WuerfelViewState.Empty;

    public event Action? Changed;

    public void ApplyContext(
        DicePageContextDto context,
        IReadOnlyDictionary<string, IReadOnlyList<BadTraitOwnerInfo>>? badTraitOwners = null)
    {
        var selectedBadTraitName = context.BadTraits.Any(trait =>
            string.Equals(trait.Name, Current.SelectedBadTraitName, StringComparison.Ordinal))
            ? Current.SelectedBadTraitName
            : null;

        Update(Current with
        {
            MasterTargets = Current.MasterTargets,
            ActiveHeroId = context.ActiveHeroId,
            ActiveHeroName = context.ActiveHeroName,
            AttributeValues =
            context.Attributes.ToDictionary(attribute => attribute.Name, attribute => attribute.Value),
            AvailableProbes = context.AvailableProbes,
            BadTraits = context.BadTraits,
            BadTraitOwners = badTraitOwners is null
                ? new Dictionary<string, IReadOnlyList<BadTraitOwnerInfo>>(StringComparer.Ordinal)
                : new Dictionary<string, IReadOnlyList<BadTraitOwnerInfo>>(badTraitOwners, StringComparer.Ordinal),
            ProbePlaceholder = context.ProbePlaceholder,
            ShowDebugForcedRolls = context.ShowDebugForcedRolls,
            SelectedAttributes = Array.Empty<string>(),
            SelectedDiceSides = Array.Empty<int>(),
            SelectedProbeValue = null,
            SelectedSpellOptionValues = Array.Empty<string>(),
            SelectedBadTraitName = selectedBadTraitName,
            Modifier = 0,
            RollText = string.Empty,
            ForcedRollsText = string.Empty,
            ProbeInfo = null,
            IsProbeInfoExpanded = false,
            ErrorMessage = null,
            ActiveArea = WuerfelArea.None,
            LastTalentRoll = null,
            LastAttributeRoll = null,
            LastBadTraitRoll = null,
            LastMasterTalentRolls = Array.Empty<MasterTalentRollTargetResultDto>(),
            LastMasterAttributeRolls = Array.Empty<MasterAttributeRollTargetResultDto>(),
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

    public void SetMasterTargets(IReadOnlyList<SessionPlayerDto> targets)
    {
        Update(Current with
        {
            MasterTargets = targets.ToArray(),
            BadTraitOwners = new Dictionary<string, IReadOnlyList<BadTraitOwnerInfo>>(StringComparer.Ordinal),
            ErrorMessage = null,
            LastMasterTalentRolls = Array.Empty<MasterTalentRollTargetResultDto>(),
            LastMasterAttributeRolls = Array.Empty<MasterAttributeRollTargetResultDto>()
        });
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
        var normalizedSelectedProbeValue = string.IsNullOrWhiteSpace(selectedProbeValue) ? null : selectedProbeValue;
        var preserveSpellOptions = ShouldPreserveSpellOptions(Current.SelectedProbeValue, normalizedSelectedProbeValue);
        Update(Current with
        {
            SelectedProbeValue = normalizedSelectedProbeValue,
            SelectedSpellOptionValues =
            preserveSpellOptions ? Current.SelectedSpellOptionValues : Array.Empty<string>(),
            ProbeInfo = null,
            IsProbeInfoExpanded = false,
            ErrorMessage = null,
            ActiveArea = normalizedSelectedProbeValue is null && Current.ActiveArea == WuerfelArea.ProbeSearch
                ? WuerfelArea.None
                : Current.ActiveArea
        });
    }

    public void ToggleSelectedSpellOption(string spellOptionValue, int? maximumSelectableOptions)
    {
        if (string.IsNullOrWhiteSpace(Current.SelectedProbeValue) ||
            !ProbeSelectionValue.TryParseSpellOption(spellOptionValue, out var parsedOption) ||
            ProbeSelectionValue.Parse(Current.SelectedProbeValue).Kind != ProbeSelectionKind.Spell)
        {
            return;
        }

        var currentBaseSelection = ProbeSelectionValue.Parse(Current.SelectedProbeValue);
        if (!string.Equals(
                TalentCatalogText.CanonicalizeName(currentBaseSelection.ProbeName),
                TalentCatalogText.CanonicalizeName(parsedOption.ProbeName),
                StringComparison.Ordinal))
        {
            return;
        }

        var selectedOptions = Current.SelectedSpellOptionValues.ToList();
        var existingIndex =
            selectedOptions.FindIndex(existingValue => AreSameSpellOption(existingValue, spellOptionValue));
        if (existingIndex >= 0)
        {
            selectedOptions.RemoveAt(existingIndex);
            Update(Current with { SelectedSpellOptionValues = selectedOptions.ToArray(), ErrorMessage = null });
            return;
        }

        if (maximumSelectableOptions.HasValue && selectedOptions.Count >= maximumSelectableOptions.Value)
        {
            var errorText = maximumSelectableOptions.Value == 1
                ? "Es ist höchstens 1 gleichzeitige Zaubermodifikation zulässig."
                : $"Es sind höchstens {maximumSelectableOptions.Value} gleichzeitige Zaubermodifikationen zulässig.";
            Update(Current with { ErrorMessage = errorText });
            return;
        }

        selectedOptions.Add(spellOptionValue);
        Update(Current with { SelectedSpellOptionValues = selectedOptions.ToArray(), ErrorMessage = null });
    }

    public void SetProbeInfo(ProbeInfoResultDto? probeInfo)
    {
        Update(Current with { ProbeInfo = probeInfo, ErrorMessage = null });
    }

    public void ClearProbeInfo()
    {
        Update(Current with { ProbeInfo = null, IsProbeInfoExpanded = false });
    }

    public void ToggleProbeInfoDetails()
    {
        Update(Current with { IsProbeInfoExpanded = !Current.IsProbeInfoExpanded });
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

    public void ApplyMasterTalentRollResults(IReadOnlyList<MasterTalentRollTargetResultDto> results)
    {
        ApplyMasterResults(
            results.Select(result => result.Result?.Equation ?? result.RequirementResult?.Equation),
            results.ToArray(),
            Array.Empty<MasterAttributeRollTargetResultDto>());
    }

    public void ApplyMasterAttributeRollResults(IReadOnlyList<MasterAttributeRollTargetResultDto> results)
    {
        ApplyMasterResults(
            results.Select(result => result.Result?.Equation),
            Array.Empty<MasterTalentRollTargetResultDto>(),
            results.ToArray());
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
            LastMasterTalentRolls = Array.Empty<MasterTalentRollTargetResultDto>(),
            LastMasterAttributeRolls = Array.Empty<MasterAttributeRollTargetResultDto>(),
            History = history,
            AnimatedDiceSides = equation.Rolls.Select(roll => roll.Sides).ToArray(),
            AnimatedDiceValues = equation.Rolls.Select(roll => roll.Value).ToArray(),
            ResultVersion = Current.ResultVersion + 1,
            ErrorMessage = null
        });
    }

    private void ApplyMasterResults(
        IEnumerable<RollEquationDto?> equations,
        IReadOnlyList<MasterTalentRollTargetResultDto> talentResults,
        IReadOnlyList<MasterAttributeRollTargetResultDto> attributeResults)
    {
        var successfulRolls = equations
            .Where(equation => equation is not null)
            .SelectMany(equation => equation!.Rolls)
            .Take(24)
            .ToArray();

        Update(Current with
        {
            LastTalentRoll = null,
            LastAttributeRoll = null,
            LastBadTraitRoll = null,
            LastMasterTalentRolls = talentResults,
            LastMasterAttributeRolls = attributeResults,
            AnimatedDiceSides = successfulRolls.Select(roll => roll.Sides).ToArray(),
            AnimatedDiceValues = successfulRolls.Select(roll => roll.Value).ToArray(),
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
            SelectedSpellOptionValues =
            clearSelectedProbe ? Array.Empty<string>() : state.SelectedSpellOptionValues,
            Modifier = 0,
            RollText = string.Empty,
            ForcedRollsText = string.Empty,
            ProbeInfo = clearSelectedProbe ? null : state.ProbeInfo,
            IsProbeInfoExpanded = clearSelectedProbe ? false : state.IsProbeInfoExpanded,
            ErrorMessage = null,
            LastTalentRoll = null,
            LastAttributeRoll = null,
            LastBadTraitRoll = null,
            LastMasterTalentRolls = Array.Empty<MasterTalentRollTargetResultDto>(),
            LastMasterAttributeRolls = Array.Empty<MasterAttributeRollTargetResultDto>(),
            PreviewVersion = state.PreviewVersion + 1
        };
    }

    private static bool ShouldPreserveSpellOptions(string? currentProbeValue, string? nextProbeValue)
    {
        var currentSelection = ProbeSelectionValue.Parse(currentProbeValue);
        var nextSelection = ProbeSelectionValue.Parse(nextProbeValue);

        return currentSelection.Kind == ProbeSelectionKind.Spell &&
               nextSelection.Kind == ProbeSelectionKind.Spell &&
               string.Equals(
                   TalentCatalogText.CanonicalizeName(currentSelection.ProbeName),
                   TalentCatalogText.CanonicalizeName(nextSelection.ProbeName),
                   StringComparison.Ordinal);
    }

    private static bool AreSameSpellOption(string leftValue, string rightValue)
    {
        if (!ProbeSelectionValue.TryParseSpellOption(leftValue, out var leftOption) ||
            !ProbeSelectionValue.TryParseSpellOption(rightValue, out var rightOption))
        {
            return string.Equals(leftValue, rightValue, StringComparison.Ordinal);
        }

        return leftOption.OptionKind == rightOption.OptionKind &&
               string.Equals(
                   TalentCatalogText.CanonicalizeName(leftOption.ProbeName),
                   TalentCatalogText.CanonicalizeName(rightOption.ProbeName),
                   StringComparison.Ordinal) &&
               string.Equals(
                   TalentCatalogText.CanonicalizeName(leftOption.OptionName),
                   TalentCatalogText.CanonicalizeName(rightOption.OptionName),
                   StringComparison.Ordinal);
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
    public IReadOnlyList<SessionPlayerDto> MasterTargets { get; init; } = Array.Empty<SessionPlayerDto>();
    public IReadOnlyDictionary<string, int> AttributeValues { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<ProbeSearchEntryDto> AvailableProbes { get; init; } = Array.Empty<ProbeSearchEntryDto>();
    public IReadOnlyList<BadTraitDto> BadTraits { get; init; } = Array.Empty<BadTraitDto>();

    public IReadOnlyDictionary<string, IReadOnlyList<BadTraitOwnerInfo>> BadTraitOwners { get; init; } =
        new Dictionary<string, IReadOnlyList<BadTraitOwnerInfo>>(StringComparer.Ordinal);

    public string ProbePlaceholder { get; init; } = "Nach Proben suchen...";
    public IReadOnlyList<string> SelectedAttributes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> SelectedDiceSides { get; init; } = Array.Empty<int>();
    public string? SelectedProbeValue { get; init; }
    public IReadOnlyList<string> SelectedSpellOptionValues { get; init; } = Array.Empty<string>();
    public string? SelectedBadTraitName { get; init; }
    public int Modifier { get; init; }
    public string RollText { get; init; } = string.Empty;
    public bool IsHiddenRoll { get; init; }
    public string ForcedRollsText { get; init; } = string.Empty;
    public ProbeInfoResultDto? ProbeInfo { get; init; }
    public bool ShowDebugForcedRolls { get; init; }
    public bool IsBusy { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsProbeInfoExpanded { get; init; }
    public WuerfelArea ActiveArea { get; init; }
    public TalentRollResultDto? LastTalentRoll { get; init; }
    public AttributeRollResultDto? LastAttributeRoll { get; init; }
    public BadTraitRollResultDto? LastBadTraitRoll { get; init; }

    public IReadOnlyList<MasterTalentRollTargetResultDto> LastMasterTalentRolls { get; init; } =
        Array.Empty<MasterTalentRollTargetResultDto>();

    public IReadOnlyList<MasterAttributeRollTargetResultDto> LastMasterAttributeRolls { get; init; } =
        Array.Empty<MasterAttributeRollTargetResultDto>();

    public IReadOnlyList<RollHistoryEntryDto> History { get; init; } = Array.Empty<RollHistoryEntryDto>();
    public IReadOnlyList<int> AnimatedDiceSides { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> AnimatedDiceValues { get; init; } = Array.Empty<int>();
    public long PreviewVersion { get; init; }
    public long ResultVersion { get; init; }

    public bool HasActiveHero => ActiveHeroId.HasValue;
    public bool IsMasterMode => MasterTargets.Count > 0;
    public bool IsMultiMasterMode => MasterTargets.Count > 1;

    public int ActiveBadTraitModifier => ActiveArea switch
    {
        _ when IsMultiMasterMode => 0,
        WuerfelArea.ProbeSearch => SelectedBadTrait?.TalentModifier ?? 0,
        WuerfelArea.Attributes => SelectedBadTrait?.AttributeModifier ?? 0,
        _ => 0
    };

    public int ActiveProbeSelectionModifier =>
        ActiveArea == WuerfelArea.ProbeSearch
            ? ResolveProbeSelectionModifier(SelectedProbeValue, SelectedSpellOptionValues)
            : 0;

    public int EffectiveModifier => Modifier + ActiveBadTraitModifier + ActiveProbeSelectionModifier;

    public bool CanRoll =>
        SelectedDiceSides.Count > 0 ||
        SelectedAttributes.Count > 0 ||
        !string.IsNullOrWhiteSpace(SelectedProbeValue);

    private BadTraitDto? SelectedBadTrait =>
        string.IsNullOrWhiteSpace(SelectedBadTraitName)
            ? null
            : BadTraits.FirstOrDefault(trait =>
                string.Equals(trait.Name, SelectedBadTraitName, StringComparison.Ordinal));

    private static int ResolveProbeSelectionModifier(
        string? selectedProbeValue,
        IReadOnlyList<string> selectedSpellOptionValues)
    {
        var baseModifier = ProbeSelectionValue.Parse(selectedProbeValue).OptionModifier;
        var spellOptionModifier = selectedSpellOptionValues
            .Select(value => ProbeSelectionValue.TryParseSpellOption(value, out var parsedOption)
                ? parsedOption.OptionModifier
                : 0)
            .DefaultIfEmpty(0)
            .Min();

        return Math.Min(baseModifier, spellOptionModifier);
    }
}

public sealed record BadTraitOwnerInfo(
    string PlayerName,
    string? HeroName,
    int Value,
    int TalentModifier,
    int AttributeModifier);
