namespace DsaWuerfelApp.Client.Services;

public sealed class ApiWuerfelRollDispatchStrategy(IWuerfelApiClient apiClient, GameClient gameClient) : IWuerfelRollDispatchStrategy
{
    public bool CanHandle()
    {
        return string.IsNullOrWhiteSpace(gameClient.CurrentSessionId);
    }

    public async Task<WuerfelRollDispatchResult<TResult>> DispatchAsync<TResult>(
        WuerfelRollCommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        var result = await command.DispatchToApiAsync(apiClient, cancellationToken);
        return new WuerfelRollDispatchResult<TResult>(result);
    }
}
