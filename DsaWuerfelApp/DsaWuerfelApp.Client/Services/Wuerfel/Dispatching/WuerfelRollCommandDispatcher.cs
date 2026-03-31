namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelRollCommandDispatcher(IEnumerable<IWuerfelRollDispatchStrategy> strategies)
{
    public async Task DispatchAsync<TResult>(
        WuerfelRollCommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        var strategy = strategies.FirstOrDefault(candidate => candidate.CanHandle())
                       ?? throw new InvalidOperationException(
                           "Es konnte kein Transportweg für den Wurf gefunden werden.");

        var result = await strategy.DispatchAsync(command, cancellationToken);
        if (result.HasImmediateResult)
        {
            command.ApplyImmediateResult(result.ImmediateResult!);
        }
    }
}