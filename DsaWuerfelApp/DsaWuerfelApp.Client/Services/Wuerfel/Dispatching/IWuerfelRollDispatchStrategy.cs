namespace DsaWuerfelApp.Client.Services;

public interface IWuerfelRollDispatchStrategy
{
    bool CanHandle();

    Task<WuerfelRollDispatchResult<TResult>> DispatchAsync<TResult>(
        WuerfelRollCommand<TResult> command,
        CancellationToken cancellationToken = default);
}