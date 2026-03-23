using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DsaWuerfelApp.Client.Components;

public partial class Dice3D : IAsyncDisposable
{
    private ElementReference _canvas;
    private DotNetObjectReference<Dice3D>? _dotNetRef;
    private IJSObjectReference? _module;

    [Parameter] public EventCallback<int> OnDiceRemoved { get; set; }

    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();

        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/dice3d.js");

            _dotNetRef = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("setDotNetRef", _dotNetRef);

            await _module.InvokeVoidAsync("init", _canvas);
        }
    }

    public async Task UpdateDice(IEnumerable<int> sides)
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("updateDice", sides);
        }
    }

    public async Task Roll(int[] results)
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("rollDice", results);
        }
    }

    [JSInvokable]
    public async Task OnDiceRemovedCallback(int index)
    {
        if (OnDiceRemoved.HasDelegate)
        {
            await OnDiceRemoved.InvokeAsync(index);
        }
    }
}