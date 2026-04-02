using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class AuthState(IAuthApiClient authApiClient)
{
    public AuthSessionDto Current { get; private set; } = new(false, null);
    public bool IsLoaded { get; private set; }

    public event Action? Changed;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoaded)
        {
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Current = await authApiClient.GetSessionAsync(cancellationToken);
        IsLoaded = true;
        Changed?.Invoke();
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await authApiClient.LogoutAsync(cancellationToken);
        Current = new AuthSessionDto(false, null);
        IsLoaded = true;
        Changed?.Invoke();
    }
}