using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace DsaWuerfelApp.Client.Services;

public class GameClient : IAsyncDisposable
{
    private readonly HubConnection _hub;
    private readonly string _userId = Guid.NewGuid().ToString("N");

    public GameClient(NavigationManager navigationManager)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/gamehub"))
            .WithAutomaticReconnect()
            .Build();

        _hub.On<string>("PlayerJoined", name => OnPlayerJoined?.Invoke(name));
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
        await _hub.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public event Action<string>? OnPlayerJoined;
    public event Action<FreeRollResultDto>? OnFreeRollResultReceived;
    public event Action<TalentRollResultDto>? OnTalentRollResultReceived;
    public event Action<AttributeRollResultDto>? OnAttributeRollResultReceived;
    public event Action<BadTraitRollResultDto>? OnBadTraitRollResultReceived;

    public async Task StartAsync()
    {
        if (!IsConnected)
        {
            await _hub.StartAsync();
        }
    }

    public async Task<string> CreateSession(string userName)
    {
        MyUserName = userName;
        var session = await _hub.InvokeAsync<SessionConnectionDto>("CreateSession", _userId, userName);
        CurrentSessionId = session.SessionId;
        return session.JoinCode;
    }

    public async Task<bool> JoinSession(string code, string userName)
    {
        MyUserName = userName;
        var session = await _hub.InvokeAsync<SessionConnectionDto?>("JoinSession", code, _userId, userName);
        CurrentSessionId = session?.SessionId;
        return session is not null;
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
}