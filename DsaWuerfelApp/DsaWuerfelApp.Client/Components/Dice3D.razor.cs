using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DsaWuerfelApp.Client.Components;

public partial class Dice3D : IAsyncDisposable
{
    private ElementReference _canvas;
    private DotNetObjectReference<Dice3D>? _dotNetRef;
    private IJSObjectReference? _module;
    private IJSObjectReference? _instance;
    private bool _disposed;
    private bool _initialized;
    private bool _initializationFailed;
    private int[]? _pendingDice;
    private int[]? _pendingRestore;
    private int[]? _pendingRoll;

    [Parameter] public EventCallback<int> OnDiceRemoved { get; set; }
    [Parameter] public EventCallback<string> ErrorOccurred { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await ReleaseAsync();
    }

    private async Task ReleaseAsync()
    {
        var instance = _instance;
        _instance = null;
        var module = _module;
        _module = null;
        try
        {
            if (instance is not null)
            {
                await instance.InvokeVoidAsync("dispose");
                await instance.DisposeAsync();
            }
            if (module is not null) await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        finally
        {
            _dotNetRef?.Dispose();
            _dotNetRef = null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _disposed || _initializationFailed) return;

        try
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/dice3d.js");
            if (_disposed) { await ReleaseAsync(); return; }
            _instance = await _module.InvokeAsync<IJSObjectReference>("createDiceScene");
            if (_disposed) { await ReleaseAsync(); return; }
            _dotNetRef = DotNetObjectReference.Create(this);
            await _instance.InvokeVoidAsync("setDotNetRef", _dotNetRef);
            if (_disposed) return;
            var instance = _instance;
            await instance.InvokeVoidAsync("init", _canvas);
            if (_disposed) return;
            _initialized = true;
            if (_pendingDice is not null) await instance.InvokeVoidAsync("updateDice", _pendingDice);
            if (_pendingRestore is not null) await instance.InvokeVoidAsync("restoreDice", _pendingRestore);
            if (_pendingRoll is not null) await instance.InvokeVoidAsync("rollDice", _pendingRoll);
            _pendingDice = _pendingRestore = _pendingRoll = null;
        }
        catch (JSDisconnectedException)
        {
            return;
        }
        catch (Exception)
        {
            _initializationFailed = true;
            await ReleaseAsync();
            if (ErrorOccurred.HasDelegate)
            {
                await ErrorOccurred.InvokeAsync("Die 3D-Würfel konnten nicht geladen werden. Das Textresultat bleibt verfügbar.");
            }
        }
    }

    public async Task UpdateDice(IEnumerable<int> sides)
    {
        if (_disposed) return;
        _pendingDice = sides.ToArray();
        _pendingRestore = null;
        _pendingRoll = null;
        if (_initialized && _instance is not null) await _instance.InvokeVoidAsync("updateDice", _pendingDice);
    }

    public async Task Roll(int[] results)
    {
        if (_disposed) return;
        _pendingRoll = results.ToArray();
        _pendingRestore = null;
        if (_initialized && _instance is not null) await _instance.InvokeVoidAsync("rollDice", _pendingRoll);
    }

    public async Task RestoreResult(int[] results)
    {
        if (_disposed) return;
        _pendingRestore = results.ToArray();
        _pendingRoll = null;
        if (_initialized && _instance is not null) await _instance.InvokeVoidAsync("restoreDice", _pendingRestore);
    }

    [JSInvokable]
    public async Task OnDiceRemovedCallback(int index)
    {
        if (!_disposed && OnDiceRemoved.HasDelegate) await OnDiceRemoved.InvokeAsync(index);
    }
}
