namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelUiOperationRunner(WuerfelState state)
{
    public async Task RunAsync(Func<Task> action)
    {
        state.BeginLoading();

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            state.SetError(exception.Message);
        }
        finally
        {
            state.EndLoading();
        }
    }
}