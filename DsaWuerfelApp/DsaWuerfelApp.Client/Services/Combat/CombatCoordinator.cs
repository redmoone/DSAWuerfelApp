using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class CombatCoordinator(
    IWuerfelApiClient apiClient,
    GameClient gameClient,
    WuerfelState wuerfelState) : IDisposable
{
    private TaskCompletionSource<CombatRollResultDto>? _pendingResult;
    private Guid? _pendingRequestId;
    private bool _attached;
    private bool _disposed;

    public void Attach()
    {
        if (_attached || _disposed)
        {
            return;
        }

        gameClient.OnCombatRollResultReceived += HandleCombatRollResultReceived;
        _attached = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_attached)
        {
            gameClient.OnCombatRollResultReceived -= HandleCombatRollResultReceived;
        }

        _pendingResult?.TrySetCanceled();
        _pendingResult = null;
        _pendingRequestId = null;
    }

    public async Task<CombatRollResultDto> RollAsync(
        CombatRollRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestId == Guid.Empty)
        {
            throw new ArgumentException("Der Kampfwurf braucht eine RequestId.", nameof(request));
        }

        Attach();
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            var result = await apiClient.RollCombatAsync(request, cancellationToken);
            ApplyOnce(result);
            return result;
        }

        await gameClient.StartAsync();
        var completion = new TaskCompletionSource<CombatRollResultDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequestId = request.RequestId;
        _pendingResult = completion;
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            await gameClient.RollCombat(request);
            var result = await completion.Task;
            return result;
        }
        finally
        {
            if (ReferenceEquals(_pendingResult, completion))
            {
                _pendingResult = null;
                _pendingRequestId = null;
            }
        }
    }

    private void HandleCombatRollResultReceived(CombatRollResultDto result)
    {
        ApplyOnce(result);
        if (_pendingRequestId == result.RequestId)
        {
            _pendingResult?.TrySetResult(result);
        }
    }

    private void ApplyOnce(CombatRollResultDto result)
    {
        if (_disposed)
        {
            return;
        }

        wuerfelState.ApplyCombatRollResult(result);
    }
}
