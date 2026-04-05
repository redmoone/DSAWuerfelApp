using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.SignalR;
using Microsoft.JSInterop;

namespace DsaWuerfelApp.Client.Services;

public sealed class SessionState : IDisposable
{
    private const string ActiveSessionStoragePrefix = "dsa.active-session:";
    private readonly AuthState _authState;
    private readonly GameClient _gameClient;
    private readonly IJSRuntime _jsRuntime;

    private readonly ISessionApiClient _sessionApiClient;
    private bool _disposed;

    public SessionState(
        ISessionApiClient sessionApiClient,
        AuthState authState,
        GameClient gameClient,
        IJSRuntime jsRuntime)
    {
        _sessionApiClient = sessionApiClient;
        _authState = authState;
        _gameClient = gameClient;
        _jsRuntime = jsRuntime;

        _authState.Changed += HandleAuthChanged;
        _gameClient.SessionChanged += HandleSessionChanged;
        _gameClient.SessionsChanged += HandleSessionsChanged;
    }

    public IReadOnlyList<SessionSummaryDto> Sessions { get; private set; } = Array.Empty<SessionSummaryDto>();
    public bool IsLoaded { get; private set; }
    public string? ActiveSessionId => _gameClient.CurrentSessionId;
    public SessionDetailsDto? ActiveSession { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _authState.Changed -= HandleAuthChanged;
        _gameClient.SessionChanged -= HandleSessionChanged;
        _gameClient.SessionsChanged -= HandleSessionsChanged;
        _disposed = true;
    }

    public event Action? Changed;
    public event Action? ActiveSessionChanged;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoaded)
        {
            return;
        }

        if (_authState.Current.IsAuthenticated)
        {
            await _gameClient.StartAsync();
        }

        await RefreshAsync(cancellationToken);
        await RestoreActiveSessionAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_authState.Current.IsAuthenticated)
        {
            Sessions = Array.Empty<SessionSummaryDto>();
            IsLoaded = true;
            Changed?.Invoke();
            return;
        }

        Sessions = await _sessionApiClient.GetMySessionsAsync(cancellationToken);
        IsLoaded = true;

        if (!string.IsNullOrWhiteSpace(ActiveSessionId) &&
            Sessions.All(session => !string.Equals(session.SessionId, ActiveSessionId, StringComparison.Ordinal)))
        {
            await ClearActiveSessionAsync(clearClientState: true);
        }

        Changed?.Invoke();
    }

    public async Task<string> CreateSessionAsync(string userName, string sessionName)
        => await ExecuteHubCallAsync(async () =>
        {
            await _gameClient.StartAsync();
            var joinCode = await _gameClient.CreateSession(userName, sessionName);

            if (!string.IsNullOrWhiteSpace(ActiveSessionId))
            {
                await LoadActiveSessionAsync(ActiveSessionId);
            }

            await RefreshAsync();
            return joinCode;
        });

    public async Task<bool> JoinSessionAsync(string joinCode, string userName)
        => await ExecuteHubCallAsync(async () =>
        {
            await _gameClient.StartAsync();
            var joined = await _gameClient.JoinSession(joinCode, userName);
            if (!joined || string.IsNullOrWhiteSpace(ActiveSessionId))
            {
                return false;
            }

            await LoadActiveSessionAsync(ActiveSessionId);
            await RefreshAsync();
            return true;
        });

    public async Task OpenSessionAsync(string sessionId)
        => await ExecuteHubCallAsync(async () =>
        {
            await _gameClient.StartAsync();
            await _gameClient.OpenSession(sessionId);
            await LoadActiveSessionAsync(sessionId);
            await RefreshAsync();
        });

    public async Task RestoreActiveSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!_authState.Current.IsAuthenticated)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(ActiveSessionId) &&
            ActiveSession is not null &&
            string.Equals(ActiveSession.SessionId, ActiveSessionId, StringComparison.Ordinal))
        {
            return;
        }

        var storedSessionId = await GetStoredActiveSessionIdAsync();
        if (string.IsNullOrWhiteSpace(storedSessionId))
        {
            return;
        }

        if (Sessions.All(session => !string.Equals(session.SessionId, storedSessionId, StringComparison.Ordinal)))
        {
            await ClearStoredActiveSessionIdAsync();
            return;
        }

        try
        {
            await _gameClient.StartAsync();
            await _gameClient.OpenSession(storedSessionId);
            await LoadActiveSessionAsync(storedSessionId, cancellationToken);
        }
        catch
        {
            await ClearActiveSessionAsync(clearClientState: true);
        }
    }

    public async Task LeaveSessionAsync(string sessionId)
        => await ExecuteHubCallAsync(async () =>
        {
            await _gameClient.StartAsync();
            var wasActive = string.Equals(ActiveSessionId, sessionId, StringComparison.Ordinal);
            await _gameClient.LeaveSession(sessionId);

            if (wasActive)
            {
                await ClearActiveSessionAsync(clearClientState: false);
            }

            await RefreshAsync();
        });

    public async Task RenameSessionAsync(string sessionId, string sessionName)
        => await ExecuteHubCallAsync(async () =>
        {
            await _gameClient.StartAsync();
            await _gameClient.RenameSession(sessionId, sessionName);
            await RefreshAsync();

            if (string.Equals(ActiveSessionId, sessionId, StringComparison.Ordinal))
            {
                await LoadActiveSessionAsync(sessionId, persistSelection: false);
            }
        });

    public async Task RenamePlayerAsync(string sessionId, string playerName)
        => await ExecuteHubCallAsync(async () =>
        {
            await _gameClient.StartAsync();
            await _gameClient.RenamePlayer(sessionId, playerName);
            await RefreshAsync();

            if (string.Equals(ActiveSessionId, sessionId, StringComparison.Ordinal))
            {
                await LoadActiveSessionAsync(sessionId, persistSelection: false);
            }
        });

    public async Task DeleteSessionAsync(string sessionId)
        => await ExecuteHubCallAsync(async () =>
        {
            await _gameClient.StartAsync();
            var wasActive = string.Equals(ActiveSessionId, sessionId, StringComparison.Ordinal);
            await _gameClient.DeleteSession(sessionId);

            if (wasActive)
            {
                await ClearActiveSessionAsync(clearClientState: false);
            }

            await RefreshAsync();
        });

    private async Task LoadActiveSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default,
        bool persistSelection = true)
    {
        var session = await _sessionApiClient.GetSessionAsync(sessionId, cancellationToken);
        await SetActiveSessionAsync(session, persistSelection);
    }

    private async Task SetActiveSessionAsync(SessionDetailsDto? session, bool persistSelection = true)
    {
        ActiveSession = session;

        if (persistSelection)
        {
            if (session is null)
            {
                await ClearStoredActiveSessionIdAsync();
            }
            else
            {
                await StoreActiveSessionIdAsync(session.SessionId);
            }
        }

        ActiveSessionChanged?.Invoke();
        Changed?.Invoke();
    }

    private async Task ClearActiveSessionAsync(bool clearClientState)
    {
        if (clearClientState)
        {
            _gameClient.ClearActiveSession();
        }

        await SetActiveSessionAsync(null);
    }

    private async Task<string?> GetStoredActiveSessionIdAsync()
    {
        var key = BuildActiveSessionStorageKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch (JSException)
        {
            return null;
        }
    }

    private async Task StoreActiveSessionIdAsync(string sessionId)
    {
        var key = BuildActiveSessionStorageKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", key, sessionId);
        }
        catch (JSException)
        {
        }
    }

    private async Task ClearStoredActiveSessionIdAsync()
    {
        var key = BuildActiveSessionStorageKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
        }
        catch (JSException)
        {
        }
    }

    private static async Task ExecuteHubCallAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (HubException exception)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }
    }

    private static async Task<T> ExecuteHubCallAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (HubException exception)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }
    }

    private string? BuildActiveSessionStorageKey()
    {
        var userId = _authState.Current.User?.Id;
        return string.IsNullOrWhiteSpace(userId)
            ? null
            : $"{ActiveSessionStoragePrefix}{userId}";
    }

    private void HandleAuthChanged()
    {
        _ = HandleAuthChangedAsync();
    }

    private async Task HandleAuthChangedAsync()
    {
        if (!_authState.Current.IsAuthenticated)
        {
            Sessions = Array.Empty<SessionSummaryDto>();
            IsLoaded = true;
            ActiveSession = null;
            ActiveSessionChanged?.Invoke();
            Changed?.Invoke();
            return;
        }

        await _gameClient.StartAsync();
        await RefreshAsync();
        await RestoreActiveSessionAsync();
    }

    private void HandleSessionChanged()
    {
        _ = HandleSessionChangedAsync();
    }

    private async Task HandleSessionChangedAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveSessionId))
        {
            await ClearActiveSessionAsync(clearClientState: false);
            await RefreshAsync();
            return;
        }

        if (ActiveSession is null ||
            !string.Equals(ActiveSession.SessionId, ActiveSessionId, StringComparison.Ordinal))
        {
            try
            {
                await LoadActiveSessionAsync(ActiveSessionId, persistSelection: true);
            }
            catch
            {
                await ClearActiveSessionAsync(clearClientState: true);
            }
        }

        await RefreshAsync();
    }

    private void HandleSessionsChanged()
    {
        _ = HandleSessionsChangedAsync();
    }

    private async Task HandleSessionsChangedAsync()
    {
        await RefreshAsync();

        if (string.IsNullOrWhiteSpace(ActiveSessionId))
        {
            return;
        }

        try
        {
            await LoadActiveSessionAsync(ActiveSessionId, persistSelection: false);
        }
        catch
        {
            await ClearActiveSessionAsync(clearClientState: true);
        }
    }
}