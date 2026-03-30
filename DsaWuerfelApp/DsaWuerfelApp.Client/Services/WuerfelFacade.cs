using System.Net.Http.Json;
using System.Text.Json;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelFacade(
    WuerfelState state,
    GameClient gameClient,
    HttpClient httpClient,
    ActiveHeroState activeHeroState)
{
    private bool _isAttached;

    public async Task AttachAsync()
    {
        if (_isAttached)
        {
            return;
        }

        await activeHeroState.EnsureLoadedAsync();

        gameClient.OnFreeRollResultReceived += HandleFreeRollResultReceived;
        gameClient.OnTalentRollResultReceived += HandleTalentRollResultReceived;
        gameClient.OnAttributeRollResultReceived += HandleAttributeRollResultReceived;
        gameClient.OnBadTraitRollResultReceived += HandleBadTraitRollResultReceived;
        activeHeroState.Changed += HandleActiveHeroChanged;
        _isAttached = true;

        await LoadContextAsync();
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        gameClient.OnFreeRollResultReceived -= HandleFreeRollResultReceived;
        gameClient.OnTalentRollResultReceived -= HandleTalentRollResultReceived;
        gameClient.OnAttributeRollResultReceived -= HandleAttributeRollResultReceived;
        gameClient.OnBadTraitRollResultReceived -= HandleBadTraitRollResultReceived;
        activeHeroState.Changed -= HandleActiveHeroChanged;
        _isAttached = false;
    }

    public Task AddDieAsync(int sides)
    {
        state.SwitchArea(WuerfelArea.FreeRoll);
        var dice = state.Current.SelectedDiceSides.ToList();
        dice.Add(sides);
        state.SetSelectedDice(dice);
        return Task.CompletedTask;
    }

    public Task RemoveDieAsync(int index)
    {
        var dice = state.Current.SelectedDiceSides.ToList();
        if (index < 0 || index >= dice.Count)
        {
            return Task.CompletedTask;
        }

        dice.RemoveAt(index);
        var attributes = state.Current.SelectedAttributes.ToList();
        if (dice.Count < attributes.Count)
        {
            attributes.RemoveAt(attributes.Count - 1);
        }

        state.SetSelectedAttributes(attributes, dice);
        return Task.CompletedTask;
    }

    public Task AddAttributeAsync(string shortName)
    {
        state.SwitchArea(WuerfelArea.Attributes);

        var attributes = state.Current.SelectedAttributes.ToList();
        var dice = state.Current.SelectedDiceSides.ToList();
        var attributeCount =
            attributes.Count(attribute => string.Equals(attribute, shortName, StringComparison.Ordinal));

        if (attributeCount >= 3)
        {
            attributes.RemoveAll(attribute => string.Equals(attribute, shortName, StringComparison.Ordinal));

            for (var index = 0; index < 3; index++)
            {
                var dieIndex = dice.IndexOf(20);
                if (dieIndex >= 0)
                {
                    dice.RemoveAt(dieIndex);
                }
            }

            state.SetSelectedAttributes(attributes, dice);
            return Task.CompletedTask;
        }

        if (attributes.Count < 3)
        {
            attributes.Add(shortName);
            dice.Add(20);
            state.SetSelectedAttributes(attributes, dice);
            return Task.CompletedTask;
        }

        attributes.RemoveAt(0);
        attributes.Add(shortName);
        state.SetSelectedAttributes(attributes, dice);
        return Task.CompletedTask;
    }

    public Task RemoveAttributeAsync(string shortName)
    {
        state.SwitchArea(WuerfelArea.Attributes);

        var attributes = state.Current.SelectedAttributes.ToList();
        var dice = state.Current.SelectedDiceSides.ToList();
        var attributeCount =
            attributes.Count(attribute => string.Equals(attribute, shortName, StringComparison.Ordinal));

        if (attributeCount == 0)
        {
            attributes.Clear();
            dice.Clear();
            attributes.AddRange([shortName, shortName, shortName]);
            dice.AddRange([20, 20, 20]);
            state.SetSelectedAttributes(attributes, dice);
            return Task.CompletedTask;
        }

        var attributeIndex = attributes.LastIndexOf(shortName);
        if (attributeIndex >= 0)
        {
            attributes.RemoveAt(attributeIndex);
        }

        var dieIndexToRemove = dice.LastIndexOf(20);
        if (dieIndexToRemove >= 0)
        {
            dice.RemoveAt(dieIndexToRemove);
        }

        state.SetSelectedAttributes(attributes, dice);
        return Task.CompletedTask;
    }

    public Task SetSelectedProbeAsync(string selectedProbeValue)
    {
        if (!string.IsNullOrWhiteSpace(selectedProbeValue))
        {
            state.SwitchArea(WuerfelArea.ProbeSearch);
            state.SetSelectedProbe(selectedProbeValue);
            return Task.CompletedTask;
        }

        state.SetSelectedProbe(null);
        return Task.CompletedTask;
    }

    public void SetSelectedBadTrait(string? selectedBadTraitName)
    {
        state.SetSelectedBadTrait(selectedBadTraitName);
    }

    public void SetModifier(int modifier)
    {
        state.SetModifier(modifier);
    }

    public void ToggleHiddenRoll()
    {
        state.ToggleHiddenRoll();
    }

    public void SetForcedRollsText(string forcedRollsText)
    {
        state.SetForcedRollsText(forcedRollsText);
    }

    public void SetForcedRollPreset(string forcedRollsText)
    {
        state.SetForcedRollsText(forcedRollsText);
    }

    public void ClearForcedRolls()
    {
        state.SetForcedRollsText(string.Empty);
    }

    public async Task ResetAsync()
    {
        state.ResetRollArea();
        await Task.CompletedTask;
    }

    public async Task ToggleProbeInfoAsync()
    {
        if (!string.IsNullOrWhiteSpace(state.Current.ProbeInfoText))
        {
            state.SetProbeInfo(null);
            return;
        }

        if (string.IsNullOrWhiteSpace(state.Current.SelectedProbeValue))
        {
            return;
        }

        state.BeginLoading();

        try
        {
            var result =
                await GetJsonAsync<ProbeInfoResultDto>(BuildProbeInfoUri(), "Probeninfo konnte nicht geladen werden.");
            state.SetProbeInfo(result.Text);
        }
        catch (Exception exception)
        {
            state.SetError(exception.Message);
        }
        finally
        {
            state.EndLoading();
        }
    }

    public async Task ExecuteCurrentRollAsync()
    {
        if (!string.IsNullOrWhiteSpace(state.Current.SelectedProbeValue))
        {
            await ExecuteTalentRollAsync();
            return;
        }

        if (state.Current.SelectedAttributes.Count > 0)
        {
            await ExecuteAttributeRollAsync();
            return;
        }

        if (state.Current.SelectedDiceSides.Count > 0)
        {
            await ExecuteFreeRollAsync();
        }
    }

    public async Task ExecuteBadTraitRollAsync()
    {
        var heroId = state.Current.ActiveHeroId;
        if (!heroId.HasValue)
        {
            state.SetError("Fuer diese Probe muss ein aktiver Held gewaehlt sein.");
            return;
        }

        if (string.IsNullOrWhiteSpace(state.Current.SelectedBadTraitName))
        {
            state.SetError("Bitte zuerst eine relevante schlechte Eigenschaft waehlen.");
            return;
        }

        var request = new BadTraitRollRequestDto(
            gameClient.CurrentSessionId,
            heroId.Value,
            state.Current.SelectedBadTraitName,
            state.Current.ForcedRollsText,
            state.Current.IsHiddenRoll);

        await ExecuteAsync(async () =>
        {
            if (CanUseOnlineSession())
            {
                await gameClient.RollBadTrait(request);
                return;
            }

            var result = await PostJsonAsync<BadTraitRollResultDto>("api/dice/bad-trait-roll", request,
                "Probe auf schlechte Eigenschaft konnte nicht ausgefuehrt werden.");
            state.ApplyBadTraitRollResult(result);
        });
    }

    private async Task LoadContextAsync()
    {
        state.BeginLoading();

        try
        {
            var result =
                await GetJsonAsync<DicePageContextDto>(BuildContextUri(),
                    "Wuerfelkontext konnte nicht geladen werden.");
            state.ApplyContext(result);
        }
        catch (Exception exception)
        {
            state.SetError(exception.Message);
        }
        finally
        {
            state.EndLoading();
        }
    }

    private async Task ExecuteFreeRollAsync()
    {
        var request = new FreeRollRequestDto(
            gameClient.CurrentSessionId,
            state.Current.SelectedDiceSides
                .GroupBy(sides => sides)
                .Select(group => new DiceRollGroupDto(group.Key, group.Count()))
                .ToArray(),
            state.Current.Modifier,
            state.Current.IsHiddenRoll);

        await ExecuteAsync(async () =>
        {
            if (CanUseOnlineSession())
            {
                await gameClient.RollFree(request);
                return;
            }

            var result = await PostJsonAsync<FreeRollResultDto>("api/dice/free-roll", request,
                "Freier Wurf konnte nicht ausgefuehrt werden.");
            state.ApplyFreeRollResult(result);
        });
    }

    private async Task ExecuteTalentRollAsync()
    {
        var heroId = state.Current.ActiveHeroId;
        if (!heroId.HasValue)
        {
            state.SetError("Fuer eine Talentprobe muss ein aktiver Held gewaehlt sein.");
            return;
        }

        if (string.IsNullOrWhiteSpace(state.Current.SelectedProbeValue))
        {
            state.SetError("Bitte zuerst ein Talent aus der Probensuche waehlen.");
            return;
        }

        var request = new TalentRollRequestDto(
            gameClient.CurrentSessionId,
            heroId.Value,
            state.Current.SelectedProbeValue,
            state.Current.Modifier,
            state.Current.SelectedBadTraitName,
            state.Current.ForcedRollsText,
            state.Current.IsHiddenRoll);

        await ExecuteAsync(async () =>
        {
            if (CanUseOnlineSession())
            {
                await gameClient.RollTalent(request);
                return;
            }

            var result = await PostJsonAsync<TalentRollResultDto>("api/dice/talent-roll", request,
                "Talentprobe konnte nicht ausgefuehrt werden.");
            state.ApplyTalentRollResult(result);
        });
    }

    private async Task ExecuteAttributeRollAsync()
    {
        var request = new AttributeRollRequestDto(
            gameClient.CurrentSessionId,
            state.Current.ActiveHeroId,
            state.Current.SelectedAttributes.ToArray(),
            state.Current.Modifier,
            state.Current.SelectedBadTraitName,
            state.Current.IsHiddenRoll);

        await ExecuteAsync(async () =>
        {
            if (CanUseOnlineSession())
            {
                await gameClient.RollAttribute(request);
                return;
            }

            var result = await PostJsonAsync<AttributeRollResultDto>("api/dice/attribute-roll", request,
                "Eigenschaftsprobe konnte nicht ausgefuehrt werden.");
            state.ApplyAttributeRollResult(result);
        });
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        state.BeginLoading();

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            state.SetError(exception.Message);
        }
        finally
        {
            state.EndLoading();
        }
    }

    private bool CanUseOnlineSession()
    {
        return gameClient.IsConnected && !string.IsNullOrWhiteSpace(gameClient.CurrentSessionId);
    }

    private string BuildContextUri()
    {
        var heroId = activeHeroState.CurrentHero?.Id;

        return heroId.HasValue
            ? $"api/dice/context?heroId={heroId.Value}"
            : "api/dice/context";
    }

    private string BuildProbeInfoUri()
    {
        var parameters = new List<string>
        {
            $"probeValue={Uri.EscapeDataString(state.Current.SelectedProbeValue ?? string.Empty)}",
            $"modifier={state.Current.Modifier}"
        };

        if (state.Current.ActiveHeroId.HasValue)
        {
            parameters.Add($"heroId={state.Current.ActiveHeroId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(state.Current.SelectedBadTraitName))
        {
            parameters.Add($"badTraitName={Uri.EscapeDataString(state.Current.SelectedBadTraitName)}");
        }

        return $"api/dice/probe-info?{string.Join("&", parameters)}";
    }

    private async Task<T> GetJsonAsync<T>(string uri, string fallbackMessage)
    {
        using var response = await httpClient.GetAsync(uri);
        await EnsureSuccessAsync(response, fallbackMessage);

        return await response.Content.ReadFromJsonAsync<T>() ??
               throw new InvalidOperationException(fallbackMessage);
    }

    private async Task<T> PostJsonAsync<T>(string uri, object request, string fallbackMessage)
    {
        using var response = await httpClient.PostAsJsonAsync(uri, request);
        await EnsureSuccessAsync(response, fallbackMessage);

        return await response.Content.ReadFromJsonAsync<T>() ??
               throw new InvalidOperationException(fallbackMessage);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string fallbackMessage)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var apiError = await TryReadApiErrorAsync(response);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(apiError) ? fallbackMessage : apiError);
    }

    private static async Task<string?> TryReadApiErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var apiError =
                JsonSerializer.Deserialize<ApiError>(content, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return string.IsNullOrWhiteSpace(apiError?.Error) ? content : apiError.Error;
        }
        catch
        {
            return null;
        }
    }

    private void HandleFreeRollResultReceived(FreeRollResultDto result)
    {
        state.ApplyFreeRollResult(result);
    }

    private void HandleTalentRollResultReceived(TalentRollResultDto result)
    {
        state.ApplyTalentRollResult(result);
    }

    private void HandleAttributeRollResultReceived(AttributeRollResultDto result)
    {
        state.ApplyAttributeRollResult(result);
    }

    private void HandleBadTraitRollResultReceived(BadTraitRollResultDto result)
    {
        state.ApplyBadTraitRollResult(result);
    }

    private void HandleActiveHeroChanged()
    {
        _ = LoadContextAsync();
    }

    private sealed record ApiError(string? Error);
}