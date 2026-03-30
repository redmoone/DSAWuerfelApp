namespace DsaWuerfelApp.Client.Services;

public sealed class SessionWuerfelRollDispatchStrategy(GameClient gameClient) : IWuerfelRollDispatchStrategy
{
    public bool CanHandle()
    {
        return gameClient.IsConnected && !string.IsNullOrWhiteSpace(gameClient.CurrentSessionId);
    }

    public async Task<WuerfelRollDispatchResult<TResult>> DispatchAsync<TResult>(
        WuerfelRollCommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        await command.DispatchToSessionAsync(gameClient);
        return new WuerfelRollDispatchResult<TResult>(default);
    }
}