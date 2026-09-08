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
    private long _loadVersion;
    private long _refreshVersion;
    private Task? _refreshTask;
    private CancellationTokenSource? _loadCancellation;
    private readonly SemaphoreSlim _storageLock = new(1, 1);
    private string? _lastStorageKey;

    private void InvalidateLoad()
    {
        ++_loadVersion;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }


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
        InvalidateLoad();
        ++_refreshVersion;
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

    public Task RefreshAsync(CancellationToken cancellationToken = default)
        => _refreshTask = RefreshCoreAsync(cancellationToken);

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (!_authState.Current.IsAuthenticated)
        {
            Sessions = Array.Empty<SessionSummaryDto>();
            IsLoaded = true;
            Changed?.Invoke();
            return;
        }

        var refreshVersion = ++_refreshVersion;
        var selectedSessionId = ActiveSessionId;
        var userId = _authState.Current.User?.Id;
        IReadOnlyList<SessionSummaryDto> sessions;
        try { sessions = await _sessionApiClient.GetMySessionsAsync(cancellationToken); }
        catch (Exception) when (_disposed || refreshVersion != _refreshVersion || userId != _authState.Current.User?.Id || !_authState.Current.IsAuthenticated) { return; }
        if (_disposed || refreshVersion != _refreshVersion || userId != _authState.Current.User?.Id || !_authState.Current.IsAuthenticated) return;
        Sessions = sessions;
        IsLoaded = true;

        if (ActiveSessionId == selectedSessionId && !string.IsNullOrWhiteSpace(ActiveSessionId) &&
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

        var version = _loadVersion;
        var userId = _authState.Current.User?.Id;
        var selectedSessionId = ActiveSessionId;
        bool IsCurrent() => !_disposed && version == _loadVersion &&
            userId == _authState.Current.User?.Id && _authState.Current.IsAuthenticated &&
            selectedSessionId == ActiveSessionId && !cancellationToken.IsCancellationRequested;
        var storedSessionId = await GetStoredActiveSessionIdAsync();
        if (!IsCurrent()) return;
        if (string.IsNullOrWhiteSpace(storedSessionId))
        {
            return;
        }

        Task? refresh;
        do
        {
            refresh = _refreshTask;
            if (refresh is not null) await refresh;
            if (!IsCurrent()) return;
        } while (refresh != _refreshTask);

        if (Sessions.All(session => !string.Equals(session.SessionId, storedSessionId, StringComparison.Ordinal)))
        {
            await ClearStoredActiveSessionIdAsync();
            return;
        }

        try
        {
            await _gameClient.StartAsync();
            if (!IsCurrent()) return;
            await _gameClient.OpenSession(storedSessionId);
            if (_disposed || !_authState.Current.IsAuthenticated || userId != _authState.Current.User?.Id || ActiveSessionId != storedSessionId) return;
            await LoadActiveSessionAsync(storedSessionId, cancellationToken);
        }
        catch
        {
            if (IsCurrent()) await ClearActiveSessionAsync(clearClientState: true);
        }
    }

    public async Task LeaveSessionAsync(string sessionId)
        => await ExecuteHubCallAsync(async () =>
        {
            await _gameClient.StartAsync();
            var wasActive = string.Equals(ActiveSessionId, sessionId, StringComparison.Ordinal);
            await _gameClient.LeaveSession(sessionId);

            if (wasActive && ActiveSessionId is null)
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
                await LoadActiveSessionAsync(sessionId);
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
                await LoadActiveSessionAsync(sessionId);
            }
        });

    public async Task DeleteSessionAsync(string sessionId)
        => await ExecuteHubCallAsync(async () =>
        {
            await _gameClient.StartAsync();
            var wasActive = string.Equals(ActiveSessionId, sessionId, StringComparison.Ordinal);
            await _gameClient.DeleteSession(sessionId);

            if (wasActive && ActiveSessionId is null)
            {
                await ClearActiveSessionAsync(clearClientState: false);
            }

            await RefreshAsync();
        });

    private async Task LoadActiveSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || sessionId != ActiveSessionId || !_authState.Current.IsAuthenticated) return;
        InvalidateLoad();
        var version = _loadVersion;
        var userId = _authState.Current.User?.Id;
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loadCancellation.Token;
        bool IsCurrent() => !_disposed && !token.IsCancellationRequested && version == _loadVersion &&
            sessionId == ActiveSessionId && _authState.Current.IsAuthenticated && userId == _authState.Current.User?.Id;
        try
        {
            var session = await _sessionApiClient.GetSessionAsync(sessionId, token);
            if (!IsCurrent()) return;
            ActiveSession = session;
            await PersistSelectionAsync(session?.SessionId, version);
            if (!IsCurrent()) return;
            ActiveSessionChanged?.Invoke();
            Changed?.Invoke();
        }
        catch (Exception) when (!IsCurrent()) { }
    }

    private async Task ClearActiveSessionAsync(bool clearClientState)
    {
        InvalidateLoad();
        ActiveSession = null;
        if (clearClientState) _gameClient.ClearActiveSession();
        var version = _loadVersion;
        await PersistSelectionAsync(null, version);
        if (version != _loadVersion || ActiveSession is not null) return;
        ActiveSessionChanged?.Invoke();
        Changed?.Invoke();
    }

    private async Task PersistSelectionAsync(string? sessionId, long version)
    {
        var key = BuildActiveSessionStorageKey() ?? _lastStorageKey;
        if (key is null) return;
        _lastStorageKey = key;
        await _storageLock.WaitAsync();
        try
        {
            if (_disposed || version != _loadVersion) return;
            if (sessionId is null) await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
            else await _jsRuntime.InvokeVoidAsync("localStorage.setItem", key, sessionId);
        }
        catch (JSException) { }
        finally { _storageLock.Release(); }
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

    private Task ClearStoredActiveSessionIdAsync() => PersistSelectionAsync(null, _loadVersion);

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
            ++_refreshVersion;
            await ClearActiveSessionAsync(clearClientState: true);
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

        if (!string.IsNullOrWhiteSpace(ActiveSessionId))
        {
            try
            {
                await LoadActiveSessionAsync(ActiveSessionId);
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
            await LoadActiveSessionAsync(ActiveSessionId);
        }
        catch
        {
            await ClearActiveSessionAsync(clearClientState: true);
        }
    }
}
