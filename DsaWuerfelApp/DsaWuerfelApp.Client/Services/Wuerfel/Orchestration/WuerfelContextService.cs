using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelContextService(
    WuerfelState state,
    ActiveHeroState activeHeroState,
    IWuerfelApiClient apiClient,
    WuerfelUiOperationRunner operationRunner)
{
    public Task LoadContextAsync()
    {
        return operationRunner.RunAsync(async () =>
        {
            var result = await apiClient.GetContextAsync(activeHeroState.CurrentHero?.Id);
            state.ApplyContext(result);
        });
    }

    public Task ToggleProbeInfoAsync()
    {
        if (!string.IsNullOrWhiteSpace(state.Current.ProbeInfoText))
        {
            state.SetProbeInfo(null);
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(state.Current.SelectedProbeValue))
        {
            return Task.CompletedTask;
        }

        var request = new ProbeInfoRequestDto(
            activeHeroState.CurrentHero?.Id ?? state.Current.ActiveHeroId,
            state.Current.SelectedProbeValue,
            state.Current.Modifier,
            state.Current.SelectedBadTraitName);

        return operationRunner.RunAsync(async () =>
        {
            var result = await apiClient.GetProbeInfoAsync(request);
            state.SetProbeInfo(result.Text);
        });
    }
}