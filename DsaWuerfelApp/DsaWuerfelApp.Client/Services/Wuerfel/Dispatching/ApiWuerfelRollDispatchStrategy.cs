namespace DsaWuerfelApp.Client.Services;

public sealed class ApiWuerfelRollDispatchStrategy(IWuerfelApiClient apiClient) : IWuerfelRollDispatchStrategy
{
    public bool CanHandle()
    {
        return true;
    }

    public async Task<WuerfelRollDispatchResult<TResult>> DispatchAsync<TResult>(
        WuerfelRollCommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        var result = await command.DispatchToApiAsync(apiClient, cancellationToken);
        return new WuerfelRollDispatchResult<TResult>(result);
    }
}