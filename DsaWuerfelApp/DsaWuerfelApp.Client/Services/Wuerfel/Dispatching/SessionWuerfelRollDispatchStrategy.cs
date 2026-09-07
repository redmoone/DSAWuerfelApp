namespace DsaWuerfelApp.Client.Services;

public sealed class SessionWuerfelRollDispatchStrategy(GameClient gameClient) : IWuerfelRollDispatchStrategy
{
    public bool CanHandle()
    {
        return !string.IsNullOrWhiteSpace(gameClient.CurrentSessionId);
    }

    public async Task<WuerfelRollDispatchResult<TResult>> DispatchAsync<TResult>(
        WuerfelRollCommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        if (!gameClient.IsConnected)
        {
            throw new InvalidOperationException(
                "Die Verbindung zur Sitzung wird wiederhergestellt. Bitte danach erneut würfeln.");
        }

        await command.DispatchToSessionAsync(gameClient);
        return new WuerfelRollDispatchResult<TResult>(default);
    }
}
