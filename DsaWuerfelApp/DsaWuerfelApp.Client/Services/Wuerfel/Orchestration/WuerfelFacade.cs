using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelFacade(
    WuerfelState state,
    GameClient gameClient,
    IWuerfelApiClient apiClient,
    SessionState sessionState,
    WuerfelSelectionService selectionService,
    WuerfelContextService contextService,
    WuerfelContextSubscription contextSubscription,
    WuerfelSignalREventBridge signalREventBridge,
    WuerfelSessionBridge sessionBridge,
    WuerfelUiOperationRunner operationRunner,
    WuerfelRollCommandDispatcher rollCommandDispatcher)
{
    private bool _isAttached;

    public async Task AttachAsync()
    {
        if (_isAttached)
        {
            return;
        }

        await sessionState.EnsureLoadedAsync();
        signalREventBridge.Attach();
        sessionBridge.Attach();
        await contextSubscription.AttachAsync();
        _isAttached = true;
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        signalREventBridge.Detach();
        sessionBridge.Detach();
        contextSubscription.Detach();
        _isAttached = false;
    }

    public async Task SetMasterTargetsAsync(IReadOnlyList<SessionPlayerDto> targets)
    {
        state.SetMasterTargets(targets);
        await contextService.LoadContextAsync(targets);
    }

    public Task AddDieAsync(int sides)
    {
        selectionService.AddDie(sides);
        return Task.CompletedTask;
    }

    public Task RemoveDieAsync(int index)
    {
        selectionService.RemoveDie(index);
        return Task.CompletedTask;
    }

    public Task AddAttributeAsync(string shortName)
    {
        selectionService.AddAttribute(shortName);
        return Task.CompletedTask;
    }

    public Task RemoveAttributeAsync(string shortName)
    {
        selectionService.RemoveAttribute(shortName);
        return Task.CompletedTask;
    }

    public async Task SetSelectedProbeAsync(string selectedProbeValue)
    {
        selectionService.SetSelectedProbe(selectedProbeValue);
        await contextService.RefreshProbeInfoAsync();
    }

    public async Task ToggleSpellOptionAsync(string spellOptionValue)
    {
        selectionService.ToggleSpellOption(spellOptionValue);
        await contextService.RefreshProbeInfoAsync();
    }

    public async Task SetSelectedBadTraitAsync(string? selectedBadTraitName)
    {
        selectionService.SetSelectedBadTrait(selectedBadTraitName);
        await contextService.RefreshProbeInfoAsync();
    }

    public async Task SetModifierAsync(int modifier)
    {
        selectionService.SetModifier(modifier);
        await contextService.RefreshProbeInfoAsync();
    }

    public void SetRollText(string rollText)
    {
        selectionService.SetRollText(rollText);
    }

    public void ToggleHiddenRoll()
    {
        selectionService.ToggleHiddenRoll();
    }

    public void SetForcedRollsText(string forcedRollsText)
    {
        selectionService.SetForcedRollsText(forcedRollsText);
    }

    public void SetForcedRollPreset(string forcedRollsText)
    {
        selectionService.SetForcedRollPreset(forcedRollsText);
    }

    public void ClearForcedRolls()
    {
        selectionService.ClearForcedRolls();
    }

    public Task ResetAsync()
    {
        selectionService.Reset();
        return Task.CompletedTask;
    }

    public Task ToggleProbeInfoDetailsAsync()
    {
        if (string.IsNullOrWhiteSpace(state.Current.SelectedProbeValue))
        {
            return Task.CompletedTask;
        }

        state.ToggleProbeInfoDetails();
        return Task.CompletedTask;
    }

    public Task ExecuteCurrentRollAsync()
    {
        if (state.Current.IsMasterMode)
        {
            if (!string.IsNullOrWhiteSpace(state.Current.SelectedProbeValue))
            {
                return ExecuteMasterTalentRollAsync();
            }

            if (state.Current.SelectedAttributes.Count > 0)
            {
                return ExecuteMasterAttributeRollAsync();
            }

            state.SetError("Im Meistermodus sind Sammelwürfe aktuell für Proben und Eigenschaften verfügbar.");
            return Task.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(state.Current.SelectedProbeValue))
        {
            return ExecuteTalentRollAsync();
        }

        if (state.Current.SelectedAttributes.Count > 0)
        {
            return ExecuteAttributeRollAsync();
        }

        return state.Current.SelectedDiceSides.Count > 0
            ? ExecuteFreeRollAsync()
            : Task.CompletedTask;
    }

    public Task ExecuteBadTraitRollAsync()
    {
        var heroId = state.Current.ActiveHeroId;
        if (!heroId.HasValue)
        {
            state.SetError("Für diese Probe muss ein aktiver Held gewählt sein.");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(state.Current.SelectedBadTraitName))
        {
            state.SetError("Bitte zuerst eine relevante schlechte Eigenschaft wählen.");
            return Task.CompletedTask;
        }

        var request = new BadTraitRollRequestDto(
            gameClient.CurrentSessionId,
            heroId.Value,
            state.Current.SelectedBadTraitName,
            state.Current.ForcedRollsText,
            state.Current.IsHiddenRoll);

        return ExecuteAsync(new WuerfelRollCommand<BadTraitRollResultDto>(
            client => client.RollBadTrait(request),
            (apiClient, cancellationToken) => apiClient.RollBadTraitAsync(request, cancellationToken),
            state.ApplyBadTraitRollResult));
    }

    private Task ExecuteFreeRollAsync()
    {
        var request = new FreeRollRequestDto(
            gameClient.CurrentSessionId,
            state.Current.SelectedDiceSides
                .GroupBy(sides => sides)
                .Select(group => new DiceRollGroupDto(group.Key, group.Count()))
                .ToArray(),
            state.Current.Modifier,
            state.Current.IsHiddenRoll);

        return ExecuteAsync(new WuerfelRollCommand<FreeRollResultDto>(
            client => client.RollFree(request),
            (apiClient, cancellationToken) => apiClient.RollFreeAsync(request, cancellationToken),
            state.ApplyFreeRollResult));
    }

    private Task ExecuteTalentRollAsync()
    {
        var heroId = state.Current.ActiveHeroId;
        if (!heroId.HasValue)
        {
            state.SetError("Für diese Probe muss ein aktiver Held gewählt sein.");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(state.Current.SelectedProbeValue))
        {
            state.SetError("Bitte zuerst ein Talent oder einen Zauber aus der Probensuche wählen.");
            return Task.CompletedTask;
        }

        var request = new TalentRollRequestDto(
            gameClient.CurrentSessionId,
            heroId.Value,
            state.Current.SelectedProbeValue,
            state.Current.Modifier,
            state.Current.SelectedBadTraitName,
            state.Current.SelectedSpellOptionValues.ToArray(),
            state.Current.ForcedRollsText,
            state.Current.IsHiddenRoll);

        return ExecuteAsync(new WuerfelRollCommand<TalentRollResultDto>(
            client => client.RollTalent(request),
            (apiClient, cancellationToken) => apiClient.RollTalentAsync(request, cancellationToken),
            state.ApplyTalentRollResult));
    }

    private Task ExecuteAttributeRollAsync()
    {
        var request = new AttributeRollRequestDto(
            gameClient.CurrentSessionId,
            state.Current.ActiveHeroId,
            state.Current.SelectedAttributes.ToArray(),
            state.Current.Modifier,
            state.Current.SelectedBadTraitName,
            state.Current.IsHiddenRoll);

        return ExecuteAsync(new WuerfelRollCommand<AttributeRollResultDto>(
            client => client.RollAttribute(request),
            (apiClient, cancellationToken) => apiClient.RollAttributeAsync(request, cancellationToken),
            state.ApplyAttributeRollResult));
    }

    private Task ExecuteMasterTalentRollAsync()
    {
        var targets = BuildMasterTargets();
        if (targets.Length == 0)
        {
            state.SetError("Bitte mindestens einen Spieler mit aktivem Helden auswählen.");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(state.Current.SelectedProbeValue))
        {
            state.SetError("Bitte zuerst ein Talent oder einen Zauber aus der Probensuche wählen.");
            return Task.CompletedTask;
        }

        var request = new MasterTalentRollRequestDto(
            targets,
            state.Current.SelectedProbeValue,
            state.Current.Modifier,
            state.Current.SelectedBadTraitName,
            state.Current.SelectedSpellOptionValues.ToArray(),
            state.Current.ForcedRollsText);

        return operationRunner.RunAsync(async () =>
        {
            var results = await apiClient.RollMasterTalentAsync(request);
            state.ApplyMasterTalentRollResults(results);
        });
    }

    private Task ExecuteMasterAttributeRollAsync()
    {
        var targets = BuildMasterTargets();
        if (targets.Length == 0)
        {
            state.SetError("Bitte mindestens einen Spieler mit aktivem Helden auswählen.");
            return Task.CompletedTask;
        }

        var request = new MasterAttributeRollRequestDto(
            targets,
            state.Current.SelectedAttributes.ToArray(),
            state.Current.Modifier,
            state.Current.SelectedBadTraitName);

        return operationRunner.RunAsync(async () =>
        {
            var results = await apiClient.RollMasterAttributeAsync(request);
            state.ApplyMasterAttributeRollResults(results);
        });
    }

    private MasterRollTargetDto[] BuildMasterTargets()
    {
        return state.Current.MasterTargets
            .Where(target => target.ActiveHeroId.HasValue)
            .Select(target => new MasterRollTargetDto(
                target.UserId,
                target.Name,
                target.ActiveHeroId!.Value,
                target.ActiveHeroName))
            .ToArray();
    }

    private Task ExecuteAsync<TResult>(WuerfelRollCommand<TResult> command)
    {
        return operationRunner.RunAsync(() => rollCommandDispatcher.DispatchAsync(command));
    }
}
