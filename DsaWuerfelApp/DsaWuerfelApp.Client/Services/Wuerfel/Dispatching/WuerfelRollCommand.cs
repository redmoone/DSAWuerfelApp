namespace DsaWuerfelApp.Client.Services;

public sealed record WuerfelRollCommand<TResult>(
    Func<GameClient, Task> DispatchToSessionAsync,
    Func<IWuerfelApiClient, CancellationToken, Task<TResult>> DispatchToApiAsync,
    Action<TResult> ApplyImmediateResult);

public sealed record WuerfelRollDispatchResult<TResult>(TResult? ImmediateResult)
{
    public bool HasImmediateResult => ImmediateResult is not null;
}