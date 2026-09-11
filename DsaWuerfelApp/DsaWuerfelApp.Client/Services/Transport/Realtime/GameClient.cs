using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace DsaWuerfelApp.Client.Services;

public class GameClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly HubConnection _hub;
    private long _selectionVersion;

    public GameClient(NavigationManager navigationManager)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/gamehub"))
            .WithAutomaticReconnect()
            .Build();

        _hub.Reconnected += HandleReconnectedAsync;
        _hub.On<string>("PlayerJoined", name => OnPlayerJoined?.Invoke(name));
        _hub.On("SessionsChanged", () => SessionsChanged?.Invoke());
        _hub.On<string, string>("SessionRenamed", HandleSessionRenamed);
        _hub.On<string>("SessionClosed", HandleSessionClosed);
        _hub.On<FreeRollResultDto>("ShowFreeRollResult", result => OnFreeRollResultReceived?.Invoke(result));
        _hub.On<TalentRollResultDto>("ShowTalentRollResult", result => OnTalentRollResultReceived?.Invoke(result));
        _hub.On<AttributeRollResultDto>("ShowAttributeRollResult",
            result => OnAttributeRollResultReceived?.Invoke(result));
        _hub.On<BadTraitRollResultDto>("ShowBadTraitRollResult",
            result => OnBadTraitRollResultReceived?.Invoke(result));
        _hub.On<CombatRollResultDto>("ShowCombatRollResult",
            result => OnCombatRollResultReceived?.Invoke(result));
        _hub.On<CombatSessionSnapshotDto>("CombatStateChanged",
            snapshot => OnCombatSessionStateReceived?.Invoke(snapshot));
    }

    public string? CurrentSessionId { get; private set; }
    public string? MyUserName { get; private set; }
    public bool IsConnected => _hub.State == HubConnectionState.Connected;

    public async ValueTask DisposeAsync()
    {
        _connectionLock.Dispose();
        await _hub.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public event Action<string>? OnPlayerJoined;
    public event Action<FreeRollResultDto>? OnFreeRollResultReceived;
    public event Action<TalentRollResultDto>? OnTalentRollResultReceived;
    public event Action<AttributeRollResultDto>? OnAttributeRollResultReceived;
    public event Action<BadTraitRollResultDto>? OnBadTraitRollResultReceived;
    public event Action<CombatRollResultDto>? OnCombatRollResultReceived;
    public event Action<CombatSessionSnapshotDto>? OnCombatSessionStateReceived;
    public event Action? SessionChanged;
    public event Action? SessionsChanged;

    public async Task StartAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (_hub.State == HubConnectionState.Disconnected)
            {
                await _hub.StartAsync();
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        var version = ++_selectionVersion;
        await _connectionLock.WaitAsync();
        try
        {
            if (_hub.State != HubConnectionState.Disconnected)
            {
                await _hub.StopAsync();
            }
        }
        finally
        {
            _connectionLock.Release();
        }

        if (version != _selectionVersion) return;
        CurrentSessionId = null;
        MyUserName = null;
        SessionChanged?.Invoke();
    }

    public async Task<string> CreateSession(string userName, string? sessionName)
    {
        var version = ++_selectionVersion;
        MyUserName = userName;
        var session = await _hub.InvokeAsync<SessionConnectionDto>("CreateSession", userName, sessionName);
        if (version == _selectionVersion) { CurrentSessionId = session.SessionId; SessionChanged?.Invoke(); }
        return session.JoinCode;
    }

    public async Task<bool> JoinSession(string code, string userName)
    {
        var version = ++_selectionVersion;
        MyUserName = userName;
        var session = await _hub.InvokeAsync<SessionConnectionDto?>("JoinSession", code, userName);
        if (session is null)
        {
            return false;
        }

        if (version != _selectionVersion) return false;
        CurrentSessionId = session.SessionId;
        SessionChanged?.Invoke();
        return true;
    }

    public async Task OpenSession(string sessionId)
    {
        var version = ++_selectionVersion;
        var session = await _hub.InvokeAsync<SessionConnectionDto>("OpenSession", sessionId);
        if (version != _selectionVersion) return;
        CurrentSessionId = session.SessionId;
        SessionChanged?.Invoke();
    }

    public async Task LeaveSession(string sessionId)
    {
        var version = ++_selectionVersion;
        await _hub.InvokeAsync("LeaveSession", sessionId);
        if (version != _selectionVersion) return;
        if (string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
        {
            CurrentSessionId = null;
            SessionChanged?.Invoke();
        }
    }

    public void ClearActiveSession()
    {
        ++_selectionVersion;
        if (string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            return;
        }

        CurrentSessionId = null;
        SessionChanged?.Invoke();
    }

    public Task RenameSession(string sessionId, string sessionName)
    {
        return _hub.InvokeAsync("RenameSession", sessionId, sessionName);
    }

    public Task RenamePlayer(string sessionId, string playerName)
    {
        return _hub.InvokeAsync("RenamePlayer", sessionId, playerName);
    }

    public Task DeleteSession(string sessionId)
    {
        return _hub.InvokeAsync("DeleteSession", sessionId);
    }

    public Task UpdateActiveHero(string sessionId, Guid? heroId, string? heroName)
    {
        return _hub.InvokeAsync("UpdateActiveHero", sessionId, heroId, heroName);
    }

    public async Task RollFree(FreeRollRequestDto request)
    {
        await _hub.InvokeAsync("RollFree", request);
    }

    public async Task RollTalent(TalentRollRequestDto request)
    {
        await _hub.InvokeAsync("RollTalent", request);
    }

    public async Task RollAttribute(AttributeRollRequestDto request)
    {
        await _hub.InvokeAsync("RollAttribute", request);
    }

    public async Task RollBadTrait(BadTraitRollRequestDto request)
    {
        await _hub.InvokeAsync("RollBadTrait", request);
    }

    public async Task RollCombat(CombatRollRequestDto request)
    {
        await _hub.InvokeAsync("RollCombat", request);
    }

    public Task<CombatSessionSnapshotDto> GetCombatSessionState(string sessionId)
    {
        return _hub.InvokeAsync<CombatSessionSnapshotDto>("GetCombatSessionState", sessionId);
    }

    public Task<CombatSessionMutationResultDto> MutateCombatSession(CombatSessionMutationRequestDto request)
    {
        return _hub.InvokeAsync<CombatSessionMutationResultDto>("MutateCombatSession", request);
    }

    private void HandleSessionRenamed(string sessionId, string _)
    {
        SessionsChanged?.Invoke();
        if (string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
        {
            SessionChanged?.Invoke();
        }
    }

    private void HandleSessionClosed(string sessionId)
    {
        if (CurrentSessionId == sessionId) ++_selectionVersion;
        if (string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
        {
            CurrentSessionId = null;
            SessionChanged?.Invoke();
        }

        SessionsChanged?.Invoke();
    }

    private async Task HandleReconnectedAsync(string? _)
    {
        var version = _selectionVersion;
        var sessionId = CurrentSessionId;
        if (string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            SessionChanged?.Invoke();
            return;
        }

        try
        {
            await _hub.InvokeAsync<SessionConnectionDto>("OpenSession", sessionId);
            if (version == _selectionVersion && sessionId == CurrentSessionId && IsConnected) SessionChanged?.Invoke();
        }
        catch
        {
            if (version != _selectionVersion || sessionId != CurrentSessionId) return;
            CurrentSessionId = null;
            SessionChanged?.Invoke();
        }
    }
}
