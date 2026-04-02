using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace DsaWuerfelApp.Client.Services;

public class GameClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly HubConnection _hub;

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

        CurrentSessionId = null;
        MyUserName = null;
        SessionChanged?.Invoke();
    }

    public async Task<string> CreateSession(string userName, string? sessionName)
    {
        MyUserName = userName;
        var session = await _hub.InvokeAsync<SessionConnectionDto>("CreateSession", userName, sessionName);
        CurrentSessionId = session.SessionId;
        SessionChanged?.Invoke();
        return session.JoinCode;
    }

    public async Task<bool> JoinSession(string code, string userName)
    {
        MyUserName = userName;
        var session = await _hub.InvokeAsync<SessionConnectionDto?>("JoinSession", code, userName);
        if (session is null)
        {
            return false;
        }

        CurrentSessionId = session.SessionId;
        SessionChanged?.Invoke();
        return true;
    }

    public async Task OpenSession(string sessionId)
    {
        var session = await _hub.InvokeAsync<SessionConnectionDto>("OpenSession", sessionId);
        CurrentSessionId = session.SessionId;
        SessionChanged?.Invoke();
    }

    public async Task LeaveSession(string sessionId)
    {
        await _hub.InvokeAsync("LeaveSession", sessionId);
        if (string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
        {
            CurrentSessionId = null;
            SessionChanged?.Invoke();
        }
    }

    public void ClearActiveSession()
    {
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

    public Task DeleteSession(string sessionId)
    {
        return _hub.InvokeAsync("DeleteSession", sessionId);
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
        if (string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
        {
            CurrentSessionId = null;
            SessionChanged?.Invoke();
        }

        SessionsChanged?.Invoke();
    }

    private async Task HandleReconnectedAsync(string? _)
    {
        if (string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            SessionChanged?.Invoke();
            return;
        }

        try
        {
            await _hub.InvokeAsync<SessionConnectionDto>("OpenSession", CurrentSessionId);
            SessionChanged?.Invoke();
        }
        catch
        {
            CurrentSessionId = null;
            SessionChanged?.Invoke();
        }
    }
}