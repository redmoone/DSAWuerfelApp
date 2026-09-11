using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace DsaWuerfelApp.Client.Components.Combat;

public partial class CombatDetailsDrawer : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Parameter] public bool Open { get; set; }
    [Parameter] public string Title { get; set; } = "Details";
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public EventCallback CloseRequested { get; set; }

    private IJSObjectReference? _focusModule;
    private ElementReference _closeButton;
    private bool _wasOpen;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Open && !_wasOpen)
        {
            _focusModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/focus-history.js");
            await _focusModule.InvokeVoidAsync("rememberActiveElement");
            await _closeButton.FocusAsync();
        }
        else if (!Open && _wasOpen && _focusModule is not null)
        {
            await _focusModule.InvokeVoidAsync("restoreActiveElement");
        }

        _wasOpen = Open;
    }

    private Task HandleKeyDown(KeyboardEventArgs args) =>
        args.Key == "Escape" ? CloseRequested.InvokeAsync() : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_focusModule is not null)
        {
            await _focusModule.DisposeAsync();
        }
    }
}
