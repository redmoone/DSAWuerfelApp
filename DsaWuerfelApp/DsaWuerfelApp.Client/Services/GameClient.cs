using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace DsaWuerfelApp.Client.Services;

public class GameClient : IAsyncDisposable
{
    private readonly HubConnection _hub;

    public GameClient(NavigationManager nav)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(nav.ToAbsoluteUri("/gamehub"))
            .WithAutomaticReconnect()
            .Build();


        _hub.On<string>("PlayerJoined", (name) =>
        {
            OnPlayerJoined?.Invoke(name);
        });

        _hub.On<RollResult>("ShowRollResult", (result) =>
        {
            OnRollResultReceived?.Invoke(result);
        });

        _hub.On<TalentProbeResult>("ShowTalentProbeResult", (result) =>
        {
            OnTalentProbeResultReceived?.Invoke(result);
        });
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
    public event Action<RollResult>? OnRollResultReceived;
    public event Action<TalentProbeResult>? OnTalentProbeResultReceived;

    public async Task StartAsync()
    {
        if (!IsConnected) await _hub.StartAsync();
    }


    public async Task<string> CreateSession(string userName)
    {
        MyUserName = userName;
        // Ruft Methode "CreateSession" im GameHub auf
        var joinCode = await _hub.InvokeAsync<string>("CreateSession", "UserId_Placeholder", userName);
        return joinCode;
    }

    public async Task<bool> JoinSession(string code, string userName)
    {
        MyUserName = userName;
        var success = await _hub.InvokeAsync<bool>("JoinSession", code, "UserId_Placeholder", userName);
        return success;
    }

    public async Task RollDice(List<DiceGroup> dice, int modifier, string sessionId)
    {
        var req = new RollRequest { Dice = dice, Modifier = modifier, SessionId = sessionId };
        await _hub.InvokeAsync("RollDice", req);
    }

    public async Task RollTalentProbe(TalentProbeRequest request)
    {
        await _hub.InvokeAsync("RollTalentProbe", request);
    }
}