using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelContextService(
    WuerfelState state,
    ActiveHeroState activeHeroState,
    IWuerfelApiClient apiClient,
    WuerfelUiOperationRunner operationRunner)
{
    private CancellationTokenSource? _probeInfoRefreshCancellation;
    private int _probeInfoRefreshVersion;

    public Task LoadContextAsync()
    {
        return operationRunner.RunAsync(async () =>
        {
            var result = await apiClient.GetContextAsync(activeHeroState.CurrentHero?.Id);
            state.ApplyContext(result);
        });
    }

    public Task RefreshProbeInfoAsync()
    {
        if (string.IsNullOrWhiteSpace(state.Current.SelectedProbeValue))
        {
            CancelProbeInfoRefresh();
            state.ClearProbeInfo();
            return Task.CompletedTask;
        }

        var request = new ProbeInfoRequestDto(
            activeHeroState.CurrentHero?.Id ?? state.Current.ActiveHeroId,
            state.Current.SelectedProbeValue,
            state.Current.Modifier,
            state.Current.SelectedBadTraitName,
            state.Current.SelectedSpellOptionValues.ToArray());
        var refreshVersion = Interlocked.Increment(ref _probeInfoRefreshVersion);
        var cancellationToken = ResetProbeInfoRefreshCancellation().Token;

        return RefreshProbeInfoAsync(request, refreshVersion, cancellationToken);
    }

    private async Task RefreshProbeInfoAsync(
        ProbeInfoRequestDto request,
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await apiClient.GetProbeInfoAsync(request, cancellationToken);
            if (ShouldIgnoreProbeInfoResult(refreshVersion, cancellationToken))
            {
                return;
            }

            state.SetProbeInfo(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (ShouldIgnoreProbeInfoResult(refreshVersion, cancellationToken))
            {
                return;
            }

            state.ClearProbeInfo();
            state.SetError(exception.Message);
        }
    }

    private CancellationTokenSource ResetProbeInfoRefreshCancellation()
    {
        CancelProbeInfoRefresh();
        _probeInfoRefreshCancellation = new CancellationTokenSource();
        return _probeInfoRefreshCancellation;
    }

    private void CancelProbeInfoRefresh()
    {
        _probeInfoRefreshCancellation?.Cancel();
        _probeInfoRefreshCancellation?.Dispose();
        _probeInfoRefreshCancellation = null;
    }

    private bool ShouldIgnoreProbeInfoResult(int refreshVersion, CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested ||
               refreshVersion != _probeInfoRefreshVersion;
    }
}