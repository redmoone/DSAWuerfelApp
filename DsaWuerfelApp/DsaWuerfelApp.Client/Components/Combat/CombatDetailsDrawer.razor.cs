using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DsaWuerfelApp.Client.Components.Combat;

public partial class CombatDetailsDrawer
{
    [Parameter] public bool Open { get; set; }
    [Parameter] public string Title { get; set; } = "Details";
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public EventCallback CloseRequested { get; set; }

    private Task HandleKeyDown(KeyboardEventArgs args) =>
        args.Key == "Escape" ? CloseRequested.InvokeAsync() : Task.CompletedTask;
}
